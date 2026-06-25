using Arch.Core;
using Microsoft.Xna.Framework.Input;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.ECS.Systems;

/// <summary>
/// Input e movimento do player no plano X/Z. Um passo por tecla pressionada.
/// Se o player anda contra uma caixa, empurra-a caso a célula seguinte esteja livre.
/// </summary>
public class MovementSystem
{
    private readonly GameWorld _world;
    private KeyboardState _previous;

    public MovementSystem(GameWorld world)
    {
        _world = world;
    }

    public void Update(KeyboardState keyboard)
    {
        (int dx, int dz) = GetFreshDirection(keyboard);
        _previous = keyboard;

        if (dx == 0 && dz == 0)
            return;

        var query = new QueryDescription().WithAll<Player, GridPosition>();
        _world.World.Query(in query, (Entity entity, ref GridPosition pos) =>
        {
            TryMovePlayer(entity, pos, dx, dz);
        });
    }

    private void TryMovePlayer(Entity player, GridPosition pos, int dx, int dz)
    {
        int targetX = pos.X + dx;
        int targetZ = pos.Z + dz;

        if (!_world.Grid.IsValid(targetX, pos.Y, targetZ))
            return;

        if (_world.Grid.IsOccupied(targetX, pos.Y, targetZ))
        {
            // Só dá pra avançar se for uma caixa empurrável.
            if (!TryPushBox(targetX, pos.Y, targetZ, dx, dz))
                return;
        }

        // Move o player.
        _world.Grid.SetOccupied(pos.X, pos.Y, pos.Z, false);
        _world.Grid.SetOccupied(targetX, pos.Y, targetZ, true);
        _world.World.Set(player, new GridPosition(targetX, pos.Y, targetZ));
    }

    /// <summary>
    /// Tenta empurrar a caixa em (x,y,z) na direção (dx,dz). Retorna true se empurrou.
    /// </summary>
    private bool TryPushBox(int x, int y, int z, int dx, int dz)
    {
        int beyondX = x + dx;
        int beyondZ = z + dz;

        if (!_world.Grid.IsValid(beyondX, y, beyondZ) || _world.Grid.IsOccupied(beyondX, y, beyondZ))
            return false;

        Entity? boxEntity = FindBoxAt(x, y, z);
        if (boxEntity is null)
            return false; // ocupado por algo que não é caixa (parede/etc)

        _world.Grid.SetOccupied(x, y, z, false);
        _world.Grid.SetOccupied(beyondX, y, beyondZ, true);
        _world.World.Set(boxEntity.Value, new GridPosition(beyondX, y, beyondZ));
        return true;
    }

    private Entity? FindBoxAt(int x, int y, int z)
    {
        Entity? found = null;
        var query = new QueryDescription().WithAll<Box, GridPosition>();
        _world.World.Query(in query, (Entity entity, ref GridPosition p) =>
        {
            if (p.X == x && p.Y == y && p.Z == z)
                found = entity;
        });
        return found;
    }

    /// <summary>
    /// Direção a partir das teclas WASD recém-pressionadas (uma só por frame), no plano X/Z.
    /// </summary>
    private (int dx, int dz) GetFreshDirection(KeyboardState k)
    {
        if (Pressed(k, Keys.A)) return (-1, 0);
        if (Pressed(k, Keys.D)) return (1, 0);
        if (Pressed(k, Keys.W)) return (0, -1);
        if (Pressed(k, Keys.S)) return (0, 1);
        return (0, 0);
    }

    private bool Pressed(KeyboardState current, Keys key)
        => current.IsKeyDown(key) && _previous.IsKeyUp(key);
}
