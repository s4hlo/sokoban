using System;
using System.Collections.Generic;
using System.Diagnostics;
using Sokoban3D.ECS.Components;
using Sokoban3D.Levels;

namespace Sokoban3D.Solver;

/// <summary>Limites da busca: estourou, o resultado volta com <see cref="SolveResult.LimitHit"/>.</summary>
public sealed class SolverLimits
{
    public int MaxNodes = 500_000;
    public TimeSpan MaxTime = TimeSpan.FromSeconds(10);

    /// <summary>Profundidade máxima do IDDFS (tier atemporal). Irrelevante no BFS.</summary>
    public int MaxDepth = 64;
}

/// <summary>Resultado da busca. <see cref="LimitHit"/> = "não sei" (parou por limite, não por prova).</summary>
public sealed class SolveResult
{
    public bool Solvable;
    public List<SolverAction> Path;
    public int NodesExplored;
    public TimeSpan Elapsed;
    public bool LimitHit;

    /// <summary>True se o nível não tem objetivo nenhum (hub): não há o que resolver.</summary>
    public bool NoObjective;
}

/// <summary>
/// Busca de solução sobre o engine real (via <see cref="SolverSim"/>), em três tiers:
/// <list type="bullet">
/// <item><b>Macro (padrão)</b>: busca sobre MACRO-AÇÕES — a caminhada por células inertes é
/// comprimida (<see cref="SolverReach"/>) e cada ação é um passo de fronteira executado pelo
/// engine (empurrão, placa, portal...). A profundidade da solução vira o nº de interações, não
/// de passos — é o que torna níveis grandes buscáveis. A ordem de exploração é melhor-primeiro
/// pela heurística de placas (<see cref="SolverReach.PressHeuristic"/>) — só prioridade, nunca
/// poda: completa e com prova de insolúvel por esgotamento; sem placas degenera pra BFS. O
/// caminho devolvido é válido (re-validado por um replay completo no engine antes de sair),
/// mas sem garantia de mínimo.</item>
/// <item><b>Com caixa <see cref="BoxType.Magnetic"/></b>: BFS por passos com as 4 direções — o
/// corpo rígido faz posição e olhar do player mudarem as transições, a caminhada deixa de ser
/// inerte e o tier macro não vale. Caminho mínimo garantido. Undo fora do conjunto: sem
/// Forget/verde ele é inverso perfeito e só revisita.</item>
/// <item><b>Com <see cref="TimelessBase"/> ou caixa <see cref="BoxType.Permanent"/></b>: + ação
/// Undo, pilhas do histórico entram no estado (dois mundos iguais com passados diferentes têm
/// futuros diferentes) e a busca vira IDDFS — as pilhas crescem com o caminho, BFS estouraria
/// memória. Um Undo que não cruzou Forget/verde volta a um estado byte-idêntico e o
/// visited poda sozinho: só undos SIGNIFICATIVOS sobrevivem.</item>
/// </list>
/// Poda comum: <c>PlayerFell</c> é estado morto (mas a meta é checada antes — pisar nela
/// conclui mesmo se o turno terminaria caído).
/// </summary>
public static class PuzzleSolver
{
    public static SolveResult Solve(Level level) => Solve(level, new SolverLimits());

    public static SolveResult Solve(Level level, SolverLimits limits)
    {
        if (level.ObjectiveSpawns.Count == 0)
            return new SolveResult { Solvable = false, NoObjective = true, Path = null };

        if (!RequiresUndoTier(level))
            return HasMagneticBox(level) ? SolveBfs(level, limits) : SolveMacro(level, limits);

        // Nível atemporal: uma solução SEM undo ainda é solução — o tier macro (completo sobre
        // o espaço sem-undo) tenta primeiro, que é ordens de magnitude mais barato. Não achou
        // (insolúvel sem undo, ou inconclusivo), o veredito fica com o IDDFS — só ele enxerga
        // os estados que o undo assimétrico alcança.
        if (!HasMagneticBox(level))
        {
            var direct = SolveMacro(level, limits);
            if (direct.Solvable)
                return direct;

            var timeless = SolveTimeless(level, limits);
            timeless.NodesExplored += direct.NodesExplored;
            return timeless;
        }

        return SolveTimeless(level, limits);
    }

    /// <summary>
    /// True se o nível tem caixa magnética: grudada, o player vira corpo rígido (modo tanque) e
    /// a caminhada deixa de ser inerte — o tier macro não vale, cai no BFS por passos.
    /// </summary>
    public static bool HasMagneticBox(Level level)
        => level.BoxSpawns.Exists(b => b.Type == BoxType.Magnetic);

    /// <summary>
    /// True se o nível pede o tier com Undo: tem base atemporal ou caixa verde — os dois
    /// pontos onde o undo é assimétrico e portanto alcança estados novos.
    /// </summary>
    public static bool RequiresUndoTier(Level level)
        => level.TimelessBaseSpawns.Count > 0
        || level.BoxSpawns.Exists(b => b.Type == BoxType.Permanent);

    // ----- Tier 0: macro-moves (BFS por interações; caminhada inerte comprimida) -----

    /// <summary>Uma macro-ação reconstituível: andar até <c>WalkTo</c> e (se houver) dar o passo
    /// <c>Dir</c>. Dir null é a caminhada final até o objetivo, que não interage com nada.</summary>
    private readonly record struct MacroEdge((int X, int Y, int Z) WalkTo, SolverAction? Dir);

    private static SolveResult SolveMacro(Level level, SolverLimits limits)
    {
        var watch = Stopwatch.StartNew();
        var sim = new SolverSim(level, trackStacks: false);
        var reach = new SolverReach(sim.Session);
        var result = new SolveResult();

        if (sim.IsSolved)
        {
            result.Solvable = true;
            result.Path = new List<SolverAction>();
            result.Elapsed = watch.Elapsed;
            return result;
        }

        // Objetivo já na região de caminhada do spawn: resolve sem busca nenhuma.
        var p0 = sim.PlayerPosition;
        var region0 = reach.Compute((p0.X, p0.Y, p0.Z));
        if (reach.ObjectiveIn(region0) is { } goal0)
        {
            result.Solvable = true;
            result.Path = ReplayMacro(level, new List<MacroEdge> { new(goal0, null) });
            result.Elapsed = watch.Elapsed;
            return result;
        }

        // Normaliza o player na âncora da região: estados que diferem só por ONDE ele está
        // dentro da mesma região são o mesmo estado de busca — junto com a compressão da
        // caminhada, é o que faz a profundidade medir decisões de puzzle, não passos.
        sim.TeleportPlayer(region0.Canonical);
        var start = sim.Capture();

        var visited = new HashSet<SolverState> { start };

        // Melhor-primeiro: prioridade = heurística de placas; seq desempata em FIFO (com
        // heurística zerada — nível sem placas — vira exatamente um BFS).
        long seq = 0;
        var queue = new PriorityQueue<SolverState, (int H, long Seq)>();
        queue.Enqueue(start, (reach.PressHeuristic(), seq++));

        // De onde cada estado veio (macro-aresta), pra reconstruir o caminho no fim.
        var parents = new Dictionary<SolverState, (SolverState From, MacroEdge Edge)>();

        while (queue.Count > 0)
        {
            var s = queue.Dequeue();

            sim.Restore(s);
            var pos = sim.PlayerPosition;
            var region = reach.Compute((pos.X, pos.Y, pos.Z));

            foreach (var (from, dir) in reach.Frontier(region))
            {
                if (result.NodesExplored >= limits.MaxNodes || watch.Elapsed >= limits.MaxTime)
                {
                    result.LimitHit = true;
                    result.Elapsed = watch.Elapsed;
                    return result;
                }

                sim.Restore(s);
                sim.TeleportPlayer(from);
                sim.Apply(dir);
                result.NodesExplored++;

                // Meta antes de tudo (pisou nela no próprio passo de fronteira).
                if (sim.IsSolved)
                {
                    var edges = ReconstructMacro(parents, s);
                    edges.Add(new MacroEdge(from, dir));
                    result.Solvable = true;
                    result.Path = ReplayMacro(level, edges);
                    result.Elapsed = watch.Elapsed;
                    return result;
                }

                SolverState n;
                bool fell = sim.Session.PlayerFell;
                if (fell)
                {
                    n = sim.Capture(); // morto: nem normaliza, só registra pro visited
                }
                else
                {
                    var lp = sim.PlayerPosition;
                    var r = reach.Compute((lp.X, lp.Y, lp.Z));

                    // O objetivo é inerte, então nunca aparece como fronteira — a vitória por
                    // caminhada é checada aqui: entrou na região, é só andar até ele.
                    if (reach.ObjectiveIn(r) is { } goal)
                    {
                        var edges = ReconstructMacro(parents, s);
                        edges.Add(new MacroEdge(from, dir));
                        edges.Add(new MacroEdge(goal, null));
                        result.Solvable = true;
                        result.Path = ReplayMacro(level, edges);
                        result.Elapsed = watch.Elapsed;
                        return result;
                    }

                    sim.TeleportPlayer(r.Canonical);
                    n = sim.Capture();
                }

                // Jogada nula (a fronteira esbarrou sem mexer em nada): nem conta como estado.
                if (n.Equals(s))
                    continue;
                if (!visited.Add(n))
                    continue;
                if (fell)
                    continue; // fica no visited, mas não expande

                parents[n] = (s, new MacroEdge(from, dir));
                queue.Enqueue(n, (reach.PressHeuristic(), seq++)); // sessão está em n
            }
        }

        result.Elapsed = watch.Elapsed;
        return result; // Solvable=false: prova (esgotou) ou LimitHit (não sei)
    }

    private static List<MacroEdge> ReconstructMacro(
        Dictionary<SolverState, (SolverState From, MacroEdge Edge)> parents, SolverState last)
    {
        var edges = new List<MacroEdge>();
        var cur = last;
        while (parents.TryGetValue(cur, out var link))
        {
            edges.Add(link.Edge);
            cur = link.From;
        }
        edges.Reverse();
        return edges;
    }

    /// <summary>
    /// Reconstitui o caminho macro em passos reais, dirigindo uma sessão nova do zero: pra cada
    /// macro-aresta, a caminhada mínima até a célula de origem e então o passo de interação —
    /// tudo pelo engine, passo a passo. É também a prova final da solução: se alguma suposição
    /// de inércia estivesse errada, o replay não terminaria resolvido e isto explode em vez de
    /// devolver um caminho falso.
    /// </summary>
    private static List<SolverAction> ReplayMacro(Level level, List<MacroEdge> edges)
    {
        var sim = new SolverSim(level, trackStacks: false);
        var reach = new SolverReach(sim.Session);
        var path = new List<SolverAction>();

        foreach (var (walkTo, dir) in edges)
        {
            var p = sim.PlayerPosition;
            foreach (var step in reach.WalkPath((p.X, p.Y, p.Z), walkTo))
            {
                sim.Apply(step);
                path.Add(step);
            }
            if (dir is { } d)
            {
                sim.Apply(d);
                path.Add(d);
            }
        }

        if (!sim.IsSolved)
            throw new InvalidOperationException(
                "Replay do caminho macro não terminou resolvido — célula tratada como inerte mudou o mundo.");
        return path;
    }

    // ----- Tier 1: BFS (caminho mínimo garantido) -----

    private static SolveResult SolveBfs(Level level, SolverLimits limits)
    {
        var watch = Stopwatch.StartNew();
        var sim = new SolverSim(level, trackStacks: false);
        var start = sim.Capture();

        var result = new SolveResult();

        if (sim.IsSolved)
        {
            result.Solvable = true;
            result.Path = new List<SolverAction>();
            result.Elapsed = watch.Elapsed;
            return result;
        }

        var visited = new HashSet<SolverState> { start };
        var queue = new Queue<SolverState>();
        queue.Enqueue(start);

        // De onde cada estado veio (pra reconstruir o caminho no fim).
        var parents = new Dictionary<SolverState, (SolverState From, SolverAction Action)>();

        while (queue.Count > 0)
        {
            if (result.NodesExplored >= limits.MaxNodes || watch.Elapsed >= limits.MaxTime)
            {
                result.LimitHit = true;
                break;
            }

            var s = queue.Dequeue();
            foreach (var action in SolverActions.Directions)
            {
                sim.Restore(s);
                sim.Apply(action);
                var n = sim.Capture();
                result.NodesExplored++;

                // Jogada nula (esbarrou sem mexer em nada): nem conta como estado.
                if (n.Equals(s))
                    continue;
                if (!visited.Add(n))
                    continue;

                // Meta antes da poda de queda: pisar nela conclui (regra do Game1, que checa
                // o objetivo independente do PlayerFell).
                if (sim.IsSolved)
                {
                    result.Solvable = true;
                    result.Path = Reconstruct(parents, s, action);
                    result.Elapsed = watch.Elapsed;
                    return result;
                }

                if (n.PlayerFell)
                    continue; // morto: fica no visited, mas não expande

                parents[n] = (s, action);
                queue.Enqueue(n);
            }
        }

        result.Elapsed = watch.Elapsed;
        return result; // Solvable=false: prova (esgotou) ou LimitHit (não sei)
    }

    private static List<SolverAction> Reconstruct(
        Dictionary<SolverState, (SolverState From, SolverAction Action)> parents,
        SolverState last, SolverAction lastAction)
    {
        var path = new List<SolverAction> { lastAction };
        var cur = last;
        while (parents.TryGetValue(cur, out var link))
        {
            path.Add(link.Action);
            cur = link.From;
        }
        path.Reverse();
        return path;
    }

    // ----- Tier 2: IDDFS com pilhas no estado (níveis com atemporal/verde) -----

    private static SolveResult SolveTimeless(Level level, SolverLimits limits)
    {
        var watch = Stopwatch.StartNew();
        var sim = new SolverSim(level, trackStacks: true);
        var start = sim.Capture();

        var result = new SolveResult();

        if (sim.IsSolved)
        {
            result.Solvable = true;
            result.Path = new List<SolverAction>();
            result.Elapsed = watch.Elapsed;
            return result;
        }

        var actions = new[]
        {
            SolverAction.Up, SolverAction.Down, SolverAction.Left, SolverAction.Right,
            SolverAction.Undo,
        };

        var path = new List<SolverAction>();

        for (int depth = 1; depth <= limits.MaxDepth; depth++)
        {
            // Melhor profundidade em que cada estado foi visto NESTA iteração: revisitar só
            // vale a pena se chegou mais raso (sobra mais orçamento de profundidade).
            var bestDepth = new Dictionary<SolverState, int> { [start] = 0 };
            bool cutoff = false;

            path.Clear();
            if (Dfs(sim, start, 0, depth, actions, bestDepth, path, limits, watch, result, ref cutoff))
            {
                result.Solvable = true;
                result.Path = new List<SolverAction>(path);
                result.Elapsed = watch.Elapsed;
                return result;
            }

            if (result.LimitHit)
                break;

            // Iteração completou sem nenhum corte por profundidade: o espaço acabou — é prova
            // de insolúvel, não adianta aprofundar.
            if (!cutoff)
                break;
        }

        result.Elapsed = watch.Elapsed;
        return result;
    }

    private static bool Dfs(
        SolverSim sim, SolverState s, int g, int limit, SolverAction[] actions,
        Dictionary<SolverState, int> bestDepth, List<SolverAction> path,
        SolverLimits limits, Stopwatch watch, SolveResult result, ref bool cutoff)
    {
        foreach (var action in actions)
        {
            if (result.NodesExplored >= limits.MaxNodes || watch.Elapsed >= limits.MaxTime)
            {
                result.LimitHit = true;
                return false;
            }

            sim.Restore(s);
            sim.Apply(action);
            var n = sim.Capture();
            result.NodesExplored++;

            if (n.Equals(s))
                continue; // jogada nula (inclui o Undo simétrico, que o estado igual denuncia)

            if (sim.IsSolved)
            {
                path.Add(action);
                return true;
            }

            if (n.PlayerFell)
                continue;

            if (g + 1 >= limit)
            {
                cutoff = true; // há espaço abaixo do corte: a próxima iteração vale a pena
                continue;
            }

            if (bestDepth.TryGetValue(n, out int seen) && seen <= g + 1)
                continue;
            bestDepth[n] = g + 1;

            path.Add(action);
            if (Dfs(sim, n, g + 1, limit, actions, bestDepth, path, limits, watch, result, ref cutoff))
                return true;
            path.RemoveAt(path.Count - 1);
        }

        return false;
    }
}
