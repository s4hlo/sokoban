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
/// Busca de solução sobre o engine real (via <see cref="SolverSim"/>), em dois tiers:
/// <list type="bullet">
/// <item><b>Sem atemporal</b>: BFS sobre estados-mundo com as 4 direções — o caminho devolvido
/// é mínimo. Undo fora do conjunto: sem Forget/verde ele é inverso perfeito e só revisita.</item>
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

        return RequiresUndoTier(level)
            ? SolveTimeless(level, limits)
            : SolveBfs(level, limits);
    }

    /// <summary>
    /// True se o nível pede o tier com Undo: tem base atemporal ou caixa verde — os dois
    /// pontos onde o undo é assimétrico e portanto alcança estados novos.
    /// </summary>
    public static bool RequiresUndoTier(Level level)
        => level.TimelessBaseSpawns.Count > 0
        || level.BoxSpawns.Exists(b => b.Type == BoxType.Permanent);

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
