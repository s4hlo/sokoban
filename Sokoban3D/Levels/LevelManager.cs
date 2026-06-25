using System;
using System.Collections.Generic;
using Arch.Core;
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
    public List<(int X, int Y, int Z, BoxType Type)> BoxSpawns = new();
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

        // Remove entidades de um nível anterior (importante pra troca de nível não duplicar).
        DestroyAllEntities();

        // Reseta grid
        _world.Grid.Clear();

        // Spawn player
        foreach (var (x, y, z) in level.PlayerSpawns)
        {
            var entity = _world.World.Create(
                new GridPosition(x, y, z),
                new Player { Speed = 1f },
                new SpawnPosition(x, y, z),
                new RenderPosition(GridView.ToWorld(_world.Grid, x, z, GridView.PieceY))
            );
            _world.Grid.SetOccupied(x, y, z, true);
        }

        // Spawn boxes
        foreach (var (x, y, z, type) in level.BoxSpawns)
        {
            var entity = _world.World.Create(
                new GridPosition(x, y, z),
                new Box { Type = type },
                new SpawnPosition(x, y, z),
                new RenderPosition(GridView.ToWorld(_world.Grid, x, z, GridView.PieceY))
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
                new Enemy { Speed = 0.5f },
                new SpawnPosition(x, y, z)
            );
            _world.Grid.SetOccupied(x, y, z, true);
        }
    }

    /// <summary>
    /// Volta todas as peças à posição inicial (R). Em vez de destruir/recriar entidades,
    /// reposiciona as existentes — assim os handles de Entity continuam válidos e o undo (Z)
    /// consegue reverter o próprio restart. Antes de reposicionar, empilha um snapshot do
    /// estado atual de TODAS as peças (inclui a caixa permanente/verde e frágeis quebradas),
    /// que é justamente o conjunto que o restart afeta — diferente de um movimento normal.
    /// </summary>
    public void Restart(History history)
    {
        if (_currentLevel == null)
            return;

        var snapshot = new List<EntityState>();
        var query = new QueryDescription().WithAll<GridPosition, SpawnPosition>();

        // 1) Snapshot do estado atual pra permitir o undo do restart. A verde NÃO entra:
        //    o undo nunca a reverte (só o R a reposiciona); por isso, ao desfazer um R,
        //    tudo volta ao estado pré-R, mas a verde permanece no spawn.
        _world.World.Query(in query, (Entity e, ref GridPosition pos) =>
        {
            if (_world.World.Has<Box>(e) && _world.World.Get<Box>(e).Type == BoxType.Permanent)
                return;

            Box? boxState = _world.World.Has<Box>(e) ? _world.World.Get<Box>(e) : null;
            snapshot.Add(new EntityState(e, pos, boxState));
        });
        history.Push(snapshot);

        // 2) Reposiciona cada peça pro spawn e reconstrói o grid do zero.
        _world.Grid.Clear();
        _world.World.Query(in query, (Entity e, ref GridPosition pos, ref SpawnPosition spawn) =>
        {
            pos = new GridPosition(spawn.X, spawn.Y, spawn.Z);

            // Caixa que tinha quebrado volta inteira.
            if (_world.World.Has<Box>(e))
            {
                var box = _world.World.Get<Box>(e);
                box.Broken = false;
                _world.World.Set(e, box);
            }

            _world.Grid.SetOccupied(spawn.X, spawn.Y, spawn.Z, true);
        });
    }

    private void DestroyAllEntities()
    {
        var toDestroy = new List<Entity>();
        var query = new QueryDescription().WithAll<GridPosition>();
        _world.World.Query(in query, (Entity entity) => toDestroy.Add(entity));

        foreach (var entity in toDestroy)
            _world.World.Destroy(entity);
    }

    public Level CurrentLevel => _currentLevel;
}
