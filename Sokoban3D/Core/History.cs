using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Core;

/// <summary>
/// Registro de uma peça que se moveu, com a posição que ela tinha ANTES do movimento.
/// </summary>
public readonly struct EntityMove
{
    public readonly Entity Entity;
    public readonly GridPosition Before;

    public EntityMove(Entity entity, GridPosition before)
    {
        Entity = entity;
        Before = before;
    }
}

/// <summary>
/// Histórico de ações pra desfazer (undo). Cada ação é o conjunto de peças que se
/// moveram juntas (player + caixas empurradas), com suas posições anteriores.
/// </summary>
public class History
{
    private readonly Stack<List<EntityMove>> _stack = new();

    public void Push(List<EntityMove> moves) => _stack.Push(moves);

    public void Clear() => _stack.Clear();

    /// <summary>
    /// Desfaz a última ação, devolvendo cada peça à posição anterior e corrigindo o grid.
    /// Retorna false se não há o que desfazer.
    /// </summary>
    public bool Undo(GameWorld world)
    {
        if (_stack.Count == 0)
            return false;

        var moves = _stack.Pop();

        // Em três fases pra não haver conflito de ocupação entre as peças que voltam:
        // 1) libera as células atuais, 2) restaura as posições, 3) reocupa as antigas.
        foreach (var m in moves)
        {
            var cur = world.World.Get<GridPosition>(m.Entity);
            world.Grid.SetOccupied(cur.X, cur.Y, cur.Z, false);
        }

        foreach (var m in moves)
            world.World.Set(m.Entity, m.Before);

        foreach (var m in moves)
            world.Grid.SetOccupied(m.Before.X, m.Before.Y, m.Before.Z, true);

        return true;
    }
}
