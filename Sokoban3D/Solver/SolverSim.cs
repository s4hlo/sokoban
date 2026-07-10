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

    // Peças móveis (query GridPosition+SpawnPosition) em ordem estável de captura — o índice
    // em SolverState.Pieces é a posição nesta lista. Capturada UMA vez no load; os handles de
    // Entity seguem válidos a sessão inteira (nem a frágil quebrada é destruída).
    private readonly List<Entity> _movers = new();

    // Terreno (Obstacle): não entra no estado, mas precisa reocupar o grid a cada Restore.
    private readonly List<Entity> _terrain = new();

    private readonly Entity _player;

    private static readonly Move[] EmptyStack = Array.Empty<Move>();

    /// <summary>A sessão scratch (exposta pra testes e pra consultas do chamador).</summary>
    public GameWorld Session => _session;

    /// <summary>
    /// Monta a sessão scratch a partir da receita. <paramref name="trackStacks"/> liga o tier
    /// atemporal: as pilhas do histórico entram no estado (e o Restore as devolve).
    /// </summary>
    public SolverSim(Level level, bool trackStacks)
    {
        _trackStacks = trackStacks;
        _session = new GameWorld(level.Width, level.Height, level.Depth);
        new LevelManager().LoadLevel(_session, level);

        var movers = new QueryDescription().WithAll<GridPosition, SpawnPosition>();
        _session.World.Query(in movers, (Entity e) => _movers.Add(e));

        var terrain = new QueryDescription().WithAll<Obstacle, GridPosition>();
        _session.World.Query(in terrain, (Entity e) => _terrain.Add(e));

        var player = _session.Spatial.First<Player>();
        _player = player ?? throw new InvalidOperationException("Nível sem player — nada a resolver.");
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

        var facing = _session.World.Get<Facing>(_player);

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
