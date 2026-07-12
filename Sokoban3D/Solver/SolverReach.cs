using System;
using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Solver;

/// <summary>
/// Alcançabilidade do player por caminhada INERTE, pro tier macro do <see cref="PuzzleSolver"/>.
/// Célula inerte é aquela em que entrar e sair andando comprovadamente não muda nada no mundo
/// além da posição do player — só essa caminhada é comprimida. Tudo que NÃO é inerte vira
/// FRONTEIRA, onde a busca executa um Step real do engine em vez de atalho:
/// <list type="bullet">
/// <item>célula ocupada — caixa comum, portal (<see cref="PortalBox"/>: o teleporte é resolvido
/// inteiro pelo PushInto), BigBox, toggle sólido;</item>
/// <item>placa de pressão (pisar/sair re-deriva os toggles do grupo);</item>
/// <item>célula sem apoio embaixo (o passo termina em queda) e o chão-morte y==0;</item>
/// <item>topo de caixa frágil (sair de cima a quebra — ver <see cref="Fragility"/>);</item>
/// <item>vizinhança horizontal de obstáculo sticky (o grude trava passos de afastamento).</item>
/// </list>
/// Trilhos não entram: seguram só a carga, nunca o player (<see cref="Rails"/>), e o
/// <see cref="LevelPortal"/> só ativa com Enter — pra caminhada os dois são chão comum.
/// Placas, stickies e objetivos nunca se movem, então seus conjuntos são capturados uma vez;
/// o resto é consultado no grid da sessão a cada flood fill.
/// </summary>
public sealed class SolverReach
{
    private readonly GameWorld _session;
    private readonly HashSet<(int X, int Y, int Z)> _plates = new();
    private readonly HashSet<(int X, int Y, int Z)> _stickyAdjacent = new();
    private readonly List<(int X, int Y, int Z)> _objectives = new();

    // Campo de distância de cada placa (BFS sobre o terreno estático, mesma altura, obstáculo
    // bloqueia; caixas/toggles ignorados — são móveis). Distância REAL de contorno, não
    // manhattan: através de parede a manhattan mente e guiaria a busca pra becos.
    private readonly Dictionary<(int X, int Y, int Z), Dictionary<(int X, int Y, int Z), int>> _plateDistance = new();

    /// <summary>Região de caminhada inerte: as células (ordem estável) e a âncora canônica.</summary>
    public sealed class Region
    {
        /// <summary>Células em ordem de descoberta do flood fill; [0] é a célula do player.</summary>
        public readonly List<(int X, int Y, int Z)> Cells = new();

        public readonly HashSet<(int X, int Y, int Z)> Set = new();

        /// <summary>
        /// A menor célula da região (ordem Y,X,Z): âncora determinística pra normalizar o player —
        /// estados que diferem só por onde ele está dentro da MESMA região são o mesmo estado.
        /// </summary>
        public (int X, int Y, int Z) Canonical;
    }

    public SolverReach(GameWorld session)
    {
        _session = session;

        var plates = new QueryDescription().WithAll<PressurePlate, GridPosition>();
        session.World.Query(in plates, (ref GridPosition p) => _plates.Add((p.X, p.Y, p.Z)));

        var obstacles = new QueryDescription().WithAll<Obstacle, GridPosition>();
        session.World.Query(in obstacles, (ref Obstacle o, ref GridPosition p) =>
        {
            if (o.Type != ObstacleType.Sticky)
                return;
            _stickyAdjacent.Add((p.X + 1, p.Y, p.Z));
            _stickyAdjacent.Add((p.X - 1, p.Y, p.Z));
            _stickyAdjacent.Add((p.X, p.Y, p.Z + 1));
            _stickyAdjacent.Add((p.X, p.Y, p.Z - 1));
        });

        var objectives = new QueryDescription().WithAll<Objective, GridPosition>();
        session.World.Query(in objectives, (ref GridPosition p) => _objectives.Add((p.X, p.Y, p.Z)));

        var terrain = new HashSet<(int X, int Y, int Z)>();
        var allObstacles = new QueryDescription().WithAll<Obstacle, GridPosition>();
        session.World.Query(in allObstacles, (ref GridPosition p) => terrain.Add((p.X, p.Y, p.Z)));
        foreach (var plate in _plates)
            _plateDistance[plate] = DistanceField(plate, terrain);
    }

    /// <summary>
    /// Distâncias de contorno a partir de <paramref name="from"/>: BFS 4-direções na mesma
    /// altura, bloqueado só pelo terreno (obstáculos e a borda do grid). Peças móveis não
    /// bloqueiam — o campo é estático, calculado uma vez.
    /// </summary>
    private Dictionary<(int X, int Y, int Z), int> DistanceField(
        (int X, int Y, int Z) from, HashSet<(int X, int Y, int Z)> terrain)
    {
        var dist = new Dictionary<(int X, int Y, int Z), int> { [from] = 0 };
        var frontier = new Queue<(int X, int Y, int Z)>();
        frontier.Enqueue(from);
        while (frontier.Count > 0)
        {
            var c = frontier.Dequeue();
            foreach (var action in SolverActions.Directions)
            {
                var (dx, dz) = SolverActions.Delta(action);
                var n = (X: c.X + dx, c.Y, Z: c.Z + dz);
                if (dist.ContainsKey(n) || !_session.Grid.IsValid(n.X, n.Y, n.Z) || terrain.Contains(n))
                    continue;
                dist[n] = dist[c] + 1;
                frontier.Enqueue(n);
            }
        }
        return dist;
    }

    /// <summary>
    /// Flood fill (4 direções, mesmo Y) das células inertes alcançáveis a partir da célula atual
    /// do player. Se a própria célula dele não é inerte (em cima de placa, de frágil...), a região
    /// é só ela: cada passo pra fora é fronteira e passa pelo engine.
    /// </summary>
    public Region Compute((int X, int Y, int Z) start)
    {
        var region = new Region();
        region.Cells.Add(start);
        region.Set.Add(start);

        if (IsInert(start.X, start.Y, start.Z))
        {
            for (int i = 0; i < region.Cells.Count; i++)
            {
                var c = region.Cells[i];
                foreach (var action in SolverActions.Directions)
                {
                    var (dx, dz) = SolverActions.Delta(action);
                    var n = (X: c.X + dx, c.Y, Z: c.Z + dz);
                    if (region.Set.Contains(n))
                        continue;
                    if (_session.Grid.IsOccupied(n.X, n.Y, n.Z)) // borda do grid conta como parede
                        continue;
                    if (!IsInert(n.X, n.Y, n.Z))
                        continue;
                    region.Cells.Add(n);
                    region.Set.Add(n);
                }
            }
        }

        region.Canonical = region.Cells[0];
        foreach (var c in region.Cells)
            if ((c.Y, c.X, c.Z).CompareTo((region.Canonical.Y, region.Canonical.X, region.Canonical.Z)) < 0)
                region.Canonical = c;

        return region;
    }

    /// <summary>
    /// As macro-ações da região: pares (célula de origem, direção) cujo alvo não é caminhada
    /// inerte — empurrão de caixa (inclui portal/BigBox) ou passo em célula especial (placa, sem
    /// apoio, topo de frágil, vizinho de sticky). Alvo ocupado por não-caixa (obstáculo, toggle
    /// sólido, inimigo) fica de fora: o engine garantidamente não faria nada.
    /// </summary>
    public List<((int X, int Y, int Z) From, SolverAction Dir)> Frontier(Region region)
    {
        var edges = new List<((int X, int Y, int Z), SolverAction)>();
        foreach (var c in region.Cells)
        {
            foreach (var action in SolverActions.Directions)
            {
                var (dx, dz) = SolverActions.Delta(action);
                var t = (X: c.X + dx, c.Y, Z: c.Z + dz);
                if (region.Set.Contains(t))
                    continue;
                if (!_session.Grid.IsValid(t.X, t.Y, t.Z))
                    continue;
                if (_session.Grid.Occupant(t.X, t.Y, t.Z) is { } occ && !_session.World.Has<Box>(occ))
                    continue;
                edges.Add((c, action));
            }
        }
        return edges;
    }

    /// <summary>
    /// Heurística de ordenação do tier macro sobre a SESSÃO ATUAL: quanto falta pra pressionar
    /// todas as placas — cada placa livre pesa fixo mais a distância de CONTORNO (campo
    /// pré-calculado) da caixa sólida mais próxima. Zero em nível sem placas (a busca degenera
    /// pra ordem FIFO). É só um guia de PRIORIDADE, nunca poda: valores ruins atrasam um estado,
    /// não o descartam — completude e prova de insolúvel por esgotamento ficam intactas.
    /// </summary>
    public int PressHeuristic()
    {
        if (_plates.Count == 0)
            return 0;

        var boxes = new List<(int X, int Y, int Z)>();
        var query = new QueryDescription().WithAll<Box, GridPosition, Solid>();
        _session.World.Query(in query, (ref GridPosition p) => boxes.Add((p.X, p.Y, p.Z)));

        int h = 0;
        foreach (var plate in _plates)
        {
            var field = _plateDistance[plate];
            if (_session.Grid.IsOccupied(plate.X, plate.Y, plate.Z))
                continue; // pressionada
            int nearest = int.MaxValue;
            foreach (var b in boxes)
                if (field.TryGetValue(b, out int d) && d < nearest)
                    nearest = d;
            if (nearest == int.MaxValue)
                nearest = 100; // nenhuma caixa no campo (outra altura/isolada): pessimista fixo
            h += 1000 + nearest;
        }
        return h;
    }

    /// <summary>A célula de algum objetivo dentro da região (vitória por caminhada), ou null.</summary>
    public (int X, int Y, int Z)? ObjectiveIn(Region region)
    {
        foreach (var o in _objectives)
            if (region.Set.Contains(o))
                return o;
        return null;
    }

    /// <summary>
    /// A caminhada mínima (BFS) de <paramref name="from"/> até <paramref name="to"/> através de
    /// células inertes, como ações do solver. Usada no replay do caminho macro; as duas células
    /// vêm da mesma região, então a rota existe — não existir é bug de inércia, e explode.
    /// </summary>
    public List<SolverAction> WalkPath((int X, int Y, int Z) from, (int X, int Y, int Z) to)
    {
        var path = new List<SolverAction>();
        if (from == to)
            return path;

        var parents = new Dictionary<(int X, int Y, int Z), ((int X, int Y, int Z) Prev, SolverAction Dir)>();
        var frontier = new Queue<(int X, int Y, int Z)>();
        var seen = new HashSet<(int X, int Y, int Z)> { from };
        frontier.Enqueue(from);

        while (frontier.Count > 0)
        {
            var c = frontier.Dequeue();
            foreach (var action in SolverActions.Directions)
            {
                var (dx, dz) = SolverActions.Delta(action);
                var n = (X: c.X + dx, c.Y, Z: c.Z + dz);
                if (seen.Contains(n))
                    continue;
                if (_session.Grid.IsOccupied(n.X, n.Y, n.Z) || !IsInert(n.X, n.Y, n.Z))
                    continue;

                parents[n] = (c, action);
                if (n == to)
                {
                    for (var cur = n; cur != from; cur = parents[cur].Prev)
                        path.Add(parents[cur].Dir);
                    path.Reverse();
                    return path;
                }

                seen.Add(n);
                frontier.Enqueue(n);
            }
        }

        throw new InvalidOperationException(
            $"Caminhada inerte de {from} até {to} não existe — inconsistência na região do solver.");
    }

    /// <summary>
    /// True se andar por (x,y,z) é comprovadamente sem efeito colateral. NÃO checa ocupação da
    /// própria célula — o chamador decide (a célula do player está ocupada por ele mesmo).
    /// </summary>
    private bool IsInert(int x, int y, int z)
    {
        // y==0 é o chão-morte: terminar um passo ali congela o player (PlayerFell).
        if (y <= 0)
            return false;

        if (_plates.Contains((x, y, z)) || _stickyAdjacent.Contains((x, y, z)))
            return false;

        // Sem apoio o passo termina em queda; apoio de frágil arma a armadilha (sair quebra).
        if (!_session.Grid.IsOccupied(x, y - 1, z))
            return false;
        if (_session.Grid.Occupant(x, y - 1, z) is { } below
            && _session.World.Has<Box>(below)
            && _session.World.Get<Box>(below).Type == BoxType.Fragile)
            return false;

        return true;
    }
}
