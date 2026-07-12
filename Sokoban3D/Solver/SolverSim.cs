using System;
using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;
using Sokoban3D.ECS.Systems;
using Sokoban3D.Levels;

namespace Sokoban3D.Solver;

/// <summary>
/// O adaptador que deixa o solver dirigir o ENGINE REAL, headless: possui uma sessão scratch
/// (<see cref="GameWorld"/> + <see cref="LevelManager.LoadLevel"/>, nenhum GraphicsDevice no
/// caminho) e traduz entre ela e o <see cref="SolverState"/> canônico. As transições são o
/// mesmo <c>MovementSystem.Step</c>/<c>Undo</c> do jogo — o solver não tem cópia própria de
/// regra nenhuma, então não há o que divergir. O padrão de uso da busca é
/// <see cref="Restore"/> → <see cref="Apply"/> → <see cref="Capture"/> por expansão de nó.
/// </summary>
public sealed class SolverSim
{
    private readonly GameWorld _session;
    private readonly MovementSystem _movement = new();
    private readonly bool _trackStacks;

    // Facing só diferencia estados quando pode mudar uma transição: com caixa magnética (modo
    // tanque) ou no tier atemporal (o olhar gravado nas pilhas volta no undo). Fora disso é
    // estado morto — capturar um valor constante funde os até 4 facings de cada mundo e devolve
    // a poda de jogada nula aos passos bloqueados que só giram a cabeça.
    private readonly bool _trackFacing;

    // Peças móveis (query GridPosition+SpawnPosition) em ordem estável de captura — o índice
    // em SolverState.Pieces é a posição nesta lista. Capturada UMA vez no load; os handles de
    // Entity seguem válidos a sessão inteira (nem a frágil quebrada é destruída).
    private readonly List<Entity> _movers = new();

    // Terreno (Obstacle): não entra no estado, mas precisa reocupar o grid a cada Restore.
    private readonly List<Entity> _terrain = new();

    // Classes de intercambialidade (índices em _movers): caixas planas do mesmo tipo são
    // indistinguíveis pro jogo, então o Capture ordena os slots de cada classe — estados que
    // diferem só por QUAL caixa idêntica está onde colapsam num só (até n! de redução). O
    // Restore devolve a atribuição canônica, que é uma realização válida do mesmo mundo. Só no
    // tier sem pilhas: com histórico por peça, a identidade volta a importar. BigBox/PortalBox
    // ficam de fora (carregam eixo/pareamento próprios).
    private readonly List<int[]> _interchangeable = new();

    private readonly Entity _player;

    private static readonly Move[] EmptyStack = Array.Empty<Move>();

    // Ordem determinística das peças de uma classe intercambiável (posição, depois solidez).
    private static readonly Comparison<SolverState.Piece> PieceOrder = (a, b) =>
        (a.Y, a.X, a.Z, a.Solid).CompareTo((b.Y, b.X, b.Z, b.Solid));

    /// <summary>A sessão scratch (exposta pra testes e pra consultas do chamador).</summary>
    public GameWorld Session => _session;

    /// <summary>
    /// Monta a sessão scratch a partir da receita. <paramref name="trackStacks"/> liga o tier
    /// atemporal: as pilhas do histórico entram no estado (e o Restore as devolve).
    /// </summary>
    public SolverSim(Level level, bool trackStacks)
    {
        _trackStacks = trackStacks;
        _trackFacing = trackStacks || level.BoxSpawns.Exists(b => b.Type == BoxType.Magnetic);
        _session = new GameWorld(level.Width, level.Height, level.Depth);
        new LevelManager().LoadLevel(_session, level);

        var movers = new QueryDescription().WithAll<GridPosition, SpawnPosition>();
        _session.World.Query(in movers, (Entity e) => _movers.Add(e));

        var terrain = new QueryDescription().WithAll<Obstacle, GridPosition>();
        _session.World.Query(in terrain, (Entity e) => _terrain.Add(e));

        var player = _session.Spatial.First<Player>();
        _player = player ?? throw new InvalidOperationException("Nível sem player — nada a resolver.");

        if (!trackStacks)
        {
            var byType = new Dictionary<BoxType, List<int>>();
            for (int i = 0; i < _movers.Count; i++)
            {
                var e = _movers[i];
                if (!_session.World.Has<Box>(e)
                    || _session.World.Has<BigBox>(e) || _session.World.Has<PortalBox>(e))
                    continue;
                var type = _session.World.Get<Box>(e).Type;
                if (!byType.TryGetValue(type, out var slots))
                    byType[type] = slots = new List<int>();
                slots.Add(i);
            }
            foreach (var slots in byType.Values)
                if (slots.Count > 1)
                    _interchangeable.Add(slots.ToArray());
        }
    }

    /// <summary>True se o player está sobre um objetivo (mesma regra do Game1).</summary>
    public bool IsSolved
    {
        get
        {
            var p = _session.World.Get<GridPosition>(_player);
            return _session.Spatial.CellWith<Objective>(p.X, p.Y, p.Z) is not null;
        }
    }

    /// <summary>Célula atual do player (pro tier macro: origem do flood fill de caminhada).</summary>
    public GridPosition PlayerPosition => _session.World.Get<GridPosition>(_player);

    /// <summary>
    /// Põe o player direto na célula dada, sem passar pelo engine — o atalho do tier macro pra
    /// caminhada inerte. O chamador garante (via <see cref="SolverReach"/>) que a célula está na
    /// região de caminhada do player, onde por definição o trajeto não muda nada no mundo: por
    /// isso não roda gravidade, placas nem histórico. Grid e componente ficam em sincronia.
    /// </summary>
    public void TeleportPlayer((int X, int Y, int Z) cell)
        => _session.Move(_player, new GridPosition(cell.X, cell.Y, cell.Z));

    /// <summary>Aplica uma ação na sessão via o engine real (um turno completo do jogo).</summary>
    public void Apply(SolverAction action)
    {
        if (action == SolverAction.Undo)
        {
            _movement.Undo(_session);
            return;
        }

        var (dx, dz) = SolverActions.Delta(action);
        _movement.Step(_session, dx, dz);
    }

    /// <summary>Fotografa a sessão no estado canônico.</summary>
    public SolverState Capture()
    {
        var pieces = new SolverState.Piece[_movers.Count];
        for (int i = 0; i < _movers.Count; i++)
        {
            var e = _movers[i];
            var p = _session.World.Get<GridPosition>(e);
            pieces[i] = new SolverState.Piece(p.X, p.Y, p.Z, _session.World.Has<Solid>(e));
        }

        // Forma canônica: dentro de cada classe de caixas idênticas, os slots ficam em ordem
        // posicional — permutações da mesma configuração viram o mesmo estado.
        foreach (var slots in _interchangeable)
        {
            var values = new SolverState.Piece[slots.Length];
            for (int k = 0; k < slots.Length; k++)
                values[k] = pieces[slots[k]];
            Array.Sort(values, PieceOrder);
            for (int k = 0; k < slots.Length; k++)
                pieces[slots[k]] = values[k];
        }

        var facing = _trackFacing ? _session.World.Get<Facing>(_player) : Facing.Default;

        Move[][] stacks = null;
        if (_trackStacks)
        {
            var snap = _session.History.Capture();
            stacks = new Move[_movers.Count][];
            for (int i = 0; i < _movers.Count; i++)
                stacks[i] = snap.TryGetValue(_movers[i], out var moves) ? moves : EmptyStack;
        }

        return new SolverState(pieces, facing.Dx, facing.Dz, _session.PlayerFell, stacks);
    }

    /// <summary>
    /// Recoloca a sessão exatamente no estado dado: posições/solidez das peças, olhar, grid
    /// reconstruído (terreno + sólidos), toggles re-derivados das placas e — no tier atemporal —
    /// as pilhas do histórico. Como o estado capturado já era assentado (pós-gravidade,
    /// pós-Resolve), a re-derivação reproduz o mesmo mundo.
    /// </summary>
    public void Restore(SolverState state)
    {
        for (int i = 0; i < _movers.Count; i++)
        {
            var e = _movers[i];
            var p = state.Pieces[i];
            _session.World.Set(e, new GridPosition(p.X, p.Y, p.Z));

            bool isSolid = _session.World.Has<Solid>(e);
            if (p.Solid && !isSolid)
                _session.World.Add(e, new Solid());
            else if (!p.Solid && isSolid)
                _session.World.Remove<Solid>(e);
        }

        _session.World.Set(_player, new Facing { Dx = state.FacingDx, Dz = state.FacingDz });

        // Grid do zero: terreno + peças sólidas nas células restauradas. Os toggles sólidos
        // são reocupados pelo Reset logo abaixo.
        _session.Grid.Clear();
        foreach (var o in _terrain)
            _session.Occupy(o);
        for (int i = 0; i < _movers.Count; i++)
            if (state.Pieces[i].Solid)
                _session.Occupy(_movers[i]);

        PressurePlateSystem.Reset(_session);

        // Depois do Reset: o valor capturado é a verdade (a re-derivação sobre posições
        // idênticas produz o mesmo resultado; isto só blinda a ordem).
        _session.PlayerFell = state.PlayerFell;

        if (_trackStacks)
        {
            var snap = new Dictionary<Entity, Move[]>();
            for (int i = 0; i < _movers.Count; i++)
                if (state.Stacks[i].Length > 0)
                    snap[_movers[i]] = state.Stacks[i];
            _session.History.Restore(snap);
        }
        else
        {
            // Tier sem atemporal: o undo não é ação, o passado não diferencia estados — zera
            // pra pilha não crescer sem limite ao longo da busca.
            _session.History.Clear();
        }
    }
}
