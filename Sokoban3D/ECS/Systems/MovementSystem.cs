using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework.Input;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.ECS.Systems;

/// <summary>
/// Input e movimento do player no plano X/Z. Um passo por tecla pressionada.
/// Ao andar contra uma fila de caixas, empurra todas se a soma dos pesos couber na
/// força do player e a célula no fim da fila estiver livre.
/// </summary>
public class MovementSystem
{
    // Força do player: soma máxima de pesos de caixas que ele consegue empurrar de uma vez.
    private const int PlayerPushStrength = 2;

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
            // Só dá pra avançar se a fila de caixas à frente puder ser empurrada.
            if (!TryPushChain(targetX, pos.Y, targetZ, dx, dz))
                return;
        }

        // Move o player (a célula alvo já está livre se houve empurrão).
        _world.Grid.SetOccupied(pos.X, pos.Y, pos.Z, false);
        _world.Grid.SetOccupied(targetX, pos.Y, targetZ, true);
        _world.World.Set(player, new GridPosition(targetX, pos.Y, targetZ));
    }

    /// <summary>
    /// Tenta empurrar a fila de caixas que começa em (x,y,z), na direção (dx,dz).
    /// Sucesso se a soma dos pesos ≤ força do player e houver célula livre no fim.
    /// </summary>
    private bool TryPushChain(int x, int y, int z, int dx, int dz)
    {
        var chain = new List<Entity>();
        int totalWeight = 0;

        int cx = x, cz = z;
        while (true)
        {
            if (!_world.Grid.IsValid(cx, y, cz))
                return false; // a fila esbarra numa parede

            if (!_world.Grid.IsOccupied(cx, y, cz))
                break; // achou célula livre: fim da fila

            Entity? box = FindBoxAt(cx, y, cz);
            if (box is null)
                return false; // ocupado por algo que não é caixa

            chain.Add(box.Value);
            totalWeight += BoxRules.Weight(_world.World.Get<Box>(box.Value).Type);

            cx += dx;
            cz += dz;
        }

        if (totalWeight > PlayerPushStrength)
            return false; // pesado demais

        // Empurra de trás pra frente, pra cada caixa cair numa célula já liberada.
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            var p = _world.World.Get<GridPosition>(chain[i]);
            int nx = p.X + dx;
            int nz = p.Z + dz;

            _world.Grid.SetOccupied(p.X, p.Y, p.Z, false);
            _world.Grid.SetOccupied(nx, p.Y, nz, true);
            _world.World.Set(chain[i], new GridPosition(nx, p.Y, nz));
        }

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
