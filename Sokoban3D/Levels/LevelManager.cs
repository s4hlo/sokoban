using System;
using System.Collections.Generic;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Levels;

/// <summary>
/// Define um nível do jogo
/// </summary>
public class Level
{
    public int Width;
    public int Height;
    public int Depth;
    public string Name;

    // Dados: posições de player, caixas, objetivos, inimigos
    public List<(int X, int Y, int Z)> PlayerSpawns = new();
    public List<(int X, int Y, int Z, int Weight)> BoxSpawns = new();
    public List<(int X, int Y, int Z)> ObjectiveSpawns = new();
    public List<(int X, int Y, int Z)> EnemySpawns = new();
}

/// <summary>
/// Gerencia carregamento e transição de níveis
/// </summary>
public class LevelManager
{
    private GameWorld _world;
    private Level _currentLevel;

    public LevelManager(GameWorld world)
    {
        _world = world;
    }

    public void LoadLevel(Level level)
    {
        _currentLevel = level;

        // Reseta grid
        _world.Grid.Clear();

        // Spawn player
        foreach (var (x, y, z) in level.PlayerSpawns)
        {
            var entity = _world.World.Create(
                new GridPosition(x, y, z),
                new Player { Speed = 1f }
            );
            _world.Grid.SetOccupied(x, y, z, true);
        }

        // Spawn boxes
        foreach (var (x, y, z, weight) in level.BoxSpawns)
        {
            var entity = _world.World.Create(
                new GridPosition(x, y, z),
                new Box { Weight = weight }
            );
            _world.Grid.SetOccupied(x, y, z, true);
        }

        // Spawn objectives
        int objId = 0;
        foreach (var (x, y, z) in level.ObjectiveSpawns)
        {
            var entity = _world.World.Create(
                new GridPosition(x, y, z),
                new Objective { Id = objId++ }
            );
            // Objetivos não ocupam espaço
        }

        // Spawn enemies
        foreach (var (x, y, z) in level.EnemySpawns)
        {
            var entity = _world.World.Create(
                new GridPosition(x, y, z),
                new Enemy { Speed = 0.5f }
            );
            _world.Grid.SetOccupied(x, y, z, true);
        }
    }

    public Level CurrentLevel => _currentLevel;
}
