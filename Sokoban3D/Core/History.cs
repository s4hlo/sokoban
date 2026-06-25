using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Core;

/// <summary>
/// Estado anterior de uma peça afetada por uma ação: posição e, se for caixa, os dados
/// da caixa (tipo + quebrada). Guardar o estado — não só a posição — deixa o undo
/// reverter mudanças como uma caixa frágil que quebrou.
/// </summary>
public readonly struct EntityState
{
    public readonly Entity Entity;
    public readonly GridPosition Position;
    public readonly Box? BoxState;

    public EntityState(Entity entity, GridPosition position, Box? boxState)
    {
        Entity = entity;
        Position = position;
        BoxState = boxState;
    }
}

/// <summary>
/// Histórico de ações pra desfazer (undo). Cada ação é o conjunto de peças afetadas
/// (player + caixas empurradas/quebradas) com seu estado anterior.
/// </summary>
public class History
{
    private readonly Stack<List<EntityState>> _stack = new();

    public void Push(List<EntityState> states) => _stack.Push(states);

    public void Clear() => _stack.Clear();

    /// <summary>
    /// Desfaz a última ação, restaurando estado e posição de cada peça e corrigindo o grid.
    /// Retorna false se não há o que desfazer.
    /// </summary>
    public bool Undo(GameWorld world)
    {
        if (_stack.Count == 0)
            return false;

        var states = _stack.Pop();

        // Em três fases pra não haver conflito de ocupação entre as peças que voltam:
        // 1) libera as células que cada peça ocupa AGORA (antes de restaurar),
        foreach (var s in states)
        {
            if (Occupies(world, s.Entity))
            {
                var cur = world.World.Get<GridPosition>(s.Entity);
                world.Grid.SetOccupied(cur.X, cur.Y, cur.Z, false);
            }
        }

        // 2) restaura posição e estado anterior,
        foreach (var s in states)
        {
            world.World.Set(s.Entity, s.Position);
            if (s.BoxState.HasValue)
                world.World.Set(s.Entity, s.BoxState.Value);
        }

        // 3) reocupa as células conforme o estado restaurado.
        foreach (var s in states)
        {
            if (Occupies(world, s.Entity))
                world.Grid.SetOccupied(s.Position.X, s.Position.Y, s.Position.Z, true);
        }

        return true;
    }

    /// <summary>Uma peça ocupa célula se for o player ou uma caixa não-quebrada.</summary>
    private static bool Occupies(GameWorld world, Entity entity)
    {
        if (world.World.Has<Player>(entity))
            return true;
        if (world.World.Has<Box>(entity))
            return !world.World.Get<Box>(entity).Broken;
        return false;
    }
}
