using System;
using System.IO;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;
using Sokoban3D.ECS.Systems;
using Sokoban3D.Levels;
using Xunit;

// O engine roda headless mas o Arch registra worlds num pool global; sessões de teste em
// paralelo não valem o risco — os testes são rápidos o bastante em série.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Sokoban3D.Tests;

/// <summary>
/// Fábrica de níveis sintéticos e utilitários comuns dos testes: montar sessões headless
/// (o mesmo caminho do jogo: GameWorld + LevelManager, nenhum GraphicsDevice) e localizar o
/// diretório de mapas reais do repositório.
/// </summary>
internal static class TestLevels
{
    /// <summary>Nível vazio com piso completo em y=0 (ninguém cai) e nada mais.</summary>
    public static Level Flat(int width, int depth, int height = 4)
    {
        var level = new Level { Name = "test", Id = -1, Width = width, Height = height, Depth = depth };
        level.FillFloor();
        return level;
    }

    /// <summary>
    /// Corredor 1D em z=1: piso completo + paredes (y=1) nas fileiras z=0 e z=2, então só a
    /// linha central é andável — bom pra testar empurrão em fila sem rota alternativa.
    /// </summary>
    public static Level Corridor(int width)
    {
        var level = Flat(width, 3);
        for (int x = 0; x < width; x++)
        {
            level.ObstacleSpawns.Add((x, 1, 0, ObstacleType.Normal));
            level.ObstacleSpawns.Add((x, 1, 2, ObstacleType.Normal));
        }
        return level;
    }

    /// <summary>Sessão headless montada pelo mesmo caminho do jogo.</summary>
    public static (GameWorld Session, MovementSystem Movement) Spawn(Level level)
    {
        var session = new GameWorld(level.Width, level.Height, level.Depth);
        new LevelManager().LoadLevel(session, level);
        return (session, new MovementSystem());
    }

    /// <summary>
    /// O diretório Sokoban3D/Maps do repositório, achado subindo a partir do diretório do
    /// binário de teste (os testes rodam de bin/Debug/...).
    /// </summary>
    public static string MapsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Sokoban3D", "Maps");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Não achei Sokoban3D/Maps subindo a partir de " + AppContext.BaseDirectory);
    }

}
