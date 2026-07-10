using Sokoban3D.Levels;
using Sokoban3D.Solver;
using Xunit;

namespace Sokoban3D.Tests;

/// <summary>
/// O oráculo de solvabilidade: TODO mapa real com objetivo tem que ser resolvível pelo
/// solver. É a rede de regressão principal — uma mudança de regra (ou um mapa editado) que
/// quebre um nível derruba o build aqui, com o id do nível na cara.
/// </summary>
public class SolvabilityOracleTests
{
    public static TheoryData<int> MapIds()
    {
        var data = new TheoryData<int>();
        foreach (int id in new LevelRepository(TestLevels.MapsDir()).ListIds())
            data.Add(id);
        return data;
    }

    [Theory]
    [MemberData(nameof(MapIds))]
    public void MapWithObjectiveIsSolvable(int id)
    {
        var level = new LevelRepository(TestLevels.MapsDir()).Load(id);

        // Níveis-hub (sem objetivo) não têm o que provar — a navegação é por portais.
        if (level.ObjectiveSpawns.Count == 0)
            return;

        var result = PuzzleSolver.Solve(level);

        Assert.False(result.LimitHit,
            $"level_{id}: busca estourou o limite ({result.NodesExplored} nós em {result.Elapsed.TotalSeconds:F1}s) — inconclusivo, considere aumentar SolverLimits.");
        Assert.True(result.Solvable,
            $"level_{id} é INSOLÚVEL ({result.NodesExplored} nós explorados, espaço esgotado).");
    }
}
