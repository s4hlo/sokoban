using Sokoban3D.ECS.Components;
using Sokoban3D.Solver;
using Xunit;

namespace Sokoban3D.Tests;

/// <summary>
/// O solver contra níveis sintéticos mínimos com resposta conhecida: caminho ótimo, prova de
/// insolúvel e as regras de peso do empurrão vistas de fora (pela solvabilidade).
/// </summary>
public class SolverBasicTests
{
    [Fact]
    public void CorridorIsSolvedInMinimalSteps()
    {
        var level = TestLevels.Flat(5, 3);
        level.PlayerSpawns.Add((1, 1, 1));
        level.ObjectiveSpawns.Add((3, 1, 1));

        var result = PuzzleSolver.Solve(level);

        Assert.True(result.Solvable);
        Assert.Equal(2, result.Path.Count); // BFS: mínimo garantido
    }

    [Fact]
    public void PushingBoxOffTheObjectiveTakesOneStep()
    {
        // A caixa está SOBRE o objetivo: um único empurrão a tira e o player pisa na meta.
        var level = TestLevels.Corridor(5);
        level.PlayerSpawns.Add((1, 1, 1));
        level.BoxSpawns.Add((2, 1, 1, BoxType.Medium));
        level.ObjectiveSpawns.Add((2, 1, 1));

        var result = PuzzleSolver.Solve(level);

        Assert.True(result.Solvable);
        Assert.Equal(1, result.Path.Count);
        Assert.Equal(SolverAction.Right, result.Path[0]);
    }

    [Fact]
    public void WalledObjectiveIsProvenUnsolvable()
    {
        var level = TestLevels.Flat(5, 5);
        level.PlayerSpawns.Add((1, 1, 1));
        level.ObjectiveSpawns.Add((3, 1, 3));
        // Mura a meta pelos 4 lados (y=1, altura do player).
        level.ObstacleSpawns.Add((2, 1, 3, ObstacleType.Normal));
        level.ObstacleSpawns.Add((4, 1, 3, ObstacleType.Normal));
        level.ObstacleSpawns.Add((3, 1, 2, ObstacleType.Normal));
        level.ObstacleSpawns.Add((3, 1, 4, ObstacleType.Normal));

        var result = PuzzleSolver.Solve(level);

        Assert.False(result.Solvable);
        Assert.False(result.LimitHit); // prova por esgotamento, não desistência
    }

    [Fact]
    public void HubWithoutObjectiveIsReportedAsSuch()
    {
        var level = TestLevels.Flat(4, 4);
        level.PlayerSpawns.Add((1, 1, 1));

        var result = PuzzleSolver.Solve(level);

        Assert.False(result.Solvable);
        Assert.True(result.NoObjective);
    }

    // ----- Regras de peso vistas pela solvabilidade (corredor sem rota alternativa) -----

    [Fact]
    public void ChainOfTwoMediumsIsPushable()
    {
        // Peso 1+1 = 2 ≤ força do player (2): a fila anda e libera a meta.
        var level = TestLevels.Corridor(7);
        level.PlayerSpawns.Add((1, 1, 1));
        level.BoxSpawns.Add((2, 1, 1, BoxType.Medium));
        level.BoxSpawns.Add((3, 1, 1, BoxType.Medium));
        level.ObjectiveSpawns.Add((2, 1, 1));

        var result = PuzzleSolver.Solve(level);

        Assert.True(result.Solvable);
        Assert.Equal(1, result.Path.Count);
    }

    [Fact]
    public void ChainOfThreeMediumsIsProvenUnsolvable()
    {
        // Peso 3 > força 2: a fila não anda e o corredor não tem volta.
        var level = TestLevels.Corridor(8);
        level.PlayerSpawns.Add((1, 1, 1));
        level.BoxSpawns.Add((2, 1, 1, BoxType.Medium));
        level.BoxSpawns.Add((3, 1, 1, BoxType.Medium));
        level.BoxSpawns.Add((4, 1, 1, BoxType.Medium));
        level.ObjectiveSpawns.Add((2, 1, 1));

        var result = PuzzleSolver.Solve(level);

        Assert.False(result.Solvable);
        Assert.False(result.LimitHit);
    }

    // ----- Tier atemporal (IDDFS + Undo no conjunto de ações) -----

    [Fact]
    public void TimelessTierStillFindsMinimalPath()
    {
        // A base atemporal liga o tier 2 (IDDFS + Undo), mas não é necessária pra solução:
        // o iterative deepening ainda tem que achar o caminho MÍNIMO.
        var level = TestLevels.Corridor(6);
        level.PlayerSpawns.Add((1, 1, 1));
        level.TimelessBaseSpawns.Add((2, 1, 1));
        level.ObjectiveSpawns.Add((4, 1, 1));

        Assert.True(PuzzleSolver.RequiresUndoTier(level));

        var result = PuzzleSolver.Solve(level);

        Assert.True(result.Solvable);
        Assert.Equal(3, result.Path.Count);
    }

    [Fact]
    public void PermanentBoxAloneEngagesUndoTier()
    {
        // A verde sem timeless também liga o tier 2: o undo é assimétrico em volta dela
        // (decisão de design — gate = TimelessBase OU Permanent).
        var level = TestLevels.Corridor(6);
        level.PlayerSpawns.Add((1, 1, 1));
        level.BoxSpawns.Add((3, 1, 1, BoxType.Permanent));
        level.ObjectiveSpawns.Add((2, 1, 1));

        Assert.True(PuzzleSolver.RequiresUndoTier(level));
        Assert.True(PuzzleSolver.Solve(level).Solvable);
    }
}
