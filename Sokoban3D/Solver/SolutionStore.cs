using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sokoban3D.Levels;

namespace Sokoban3D.Solver;

/// <summary>
/// Os certificados de solução em disco: <c>Sokoban3D.Tests/Solutions/level_N.moves</c>, uma
/// sequência de U/D/L/R (e Z pra undo) na convenção do <see cref="SolverAction"/>, whitespace
/// ignorado. Fonte única de load/save/replay pros três consumidores: o oráculo de testes (prova
/// de solvabilidade quando a busca é inconclusiva), o <see cref="SolverTool"/> (o P toca o
/// certificado como fallback da busca) e o <see cref="SolutionRecorder"/> (grava a vitória do
/// player). Fora do repositório (build distribuído) o diretório não existe e tudo degrada pra
/// "não tem certificado".
/// </summary>
public static class SolutionStore
{
    /// <summary>Carrega o certificado do nível. False se não existe (ou sem repositório por
    /// perto); arquivo presente mas ilegível explode — conteúdo à mão merece erro alto.</summary>
    public static bool TryLoad(int levelId, out List<SolverAction> moves)
    {
        moves = new List<SolverAction>();
        var file = FileFor(levelId);
        if (file is null || !File.Exists(file))
            return false;

        foreach (char c in File.ReadAllText(file))
        {
            if (char.IsWhiteSpace(c))
                continue;
            moves.Add(c switch
            {
                'U' => SolverAction.Up,
                'D' => SolverAction.Down,
                'L' => SolverAction.Left,
                'R' => SolverAction.Right,
                'Z' => SolverAction.Undo,
                _ => throw new InvalidDataException($"{file}: movimento inválido '{c}' (esperado U/D/L/R/Z)"),
            });
        }
        return true;
    }

    /// <summary>Grava (ou sobrescreve) o certificado do nível. A política de QUANDO gravar é do
    /// chamador (<see cref="SolutionRecorder"/>); aqui é só a serialização.</summary>
    public static void Save(int levelId, IReadOnlyList<SolverAction> moves)
    {
        var file = FileFor(levelId);
        if (file is null)
            return; // sem repositório por perto: nada a fazer

        var sb = new StringBuilder(moves.Count + 1);
        foreach (var move in moves)
            sb.Append(move switch
            {
                SolverAction.Up => 'U',
                SolverAction.Down => 'D',
                SolverAction.Left => 'L',
                SolverAction.Right => 'R',
                _ => 'Z',
            });
        sb.Append('\n');

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, sb.ToString());
    }

    /// <summary>Replaya os movimentos no engine real (headless) e diz se terminam no objetivo —
    /// a validade de um certificado é isto, nada além.</summary>
    public static bool Solves(Level level, List<SolverAction> moves)
    {
        var sim = new SolverSim(level, trackStacks: false);
        foreach (var move in moves)
            sim.Apply(move);
        return sim.IsSolved;
    }

    /// <summary>O caminho do certificado do nível, ou null fora do repositório. O diretório é
    /// achado subindo a partir do binário (jogo e testes rodam de bin/...).</summary>
    private static string FileFor(int levelId)
    {
        if (levelId < 0)
            return null;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var tests = Path.Combine(dir.FullName, "Sokoban3D.Tests");
            if (Directory.Exists(tests))
                return Path.Combine(tests, "Solutions", $"level_{levelId}.moves");
            dir = dir.Parent;
        }
        return null;
    }
}
