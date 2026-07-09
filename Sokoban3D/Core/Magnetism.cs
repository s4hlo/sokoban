using System;
using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Core;

/// <summary>
/// Regras do magnetismo: uma caixa <see cref="BoxType.Magnetic"/> horizontalmente adjacente ao
/// player (mesmo Y, 4 vizinhos) está GRUDADA nele, no lado em que encostou. É estado DERIVADO
/// das posições — nada é gravado em componente nem no histórico: mover, desfazer ou resetar
/// reposiciona as peças e o grude se re-deriva sozinho (um undo que volta pra antes do encontro
/// solta a caixa naturalmente). Enquanto houver caixa grudada, o player se move em modo tanque
/// com ela como parte rígida do corpo (<see cref="ECS.Systems.MovementSystem"/>) e ela não cai
/// (<see cref="Gravity"/> — o grude a segura pairando sobre buracos).
/// </summary>
public static class Magnetism
{
    /// <summary>
    /// As caixas magnéticas grudadas no player agora, com o offset (célula da caixa menos a do
    /// player) de cada uma. Vazio se não há player ou nenhuma adjacente.
    /// </summary>
    public static List<(Entity Box, int Ox, int Oz)> Attached(GameWorld session)
    {
        var result = new List<(Entity Box, int Ox, int Oz)>();
        var player = session.Spatial.First<Player>();
        if (player is null)
            return result;
        var p = session.World.Get<GridPosition>(player.Value);

        var query = new QueryDescription().WithAll<Box, GridPosition, Solid>();
        session.World.Query(in query, (Entity e, ref Box b, ref GridPosition bp) =>
        {
            if (b.Type != BoxType.Magnetic || bp.Y != p.Y)
                return;
            int ox = bp.X - p.X, oz = bp.Z - p.Z;
            if (Math.Abs(ox) + Math.Abs(oz) == 1)
                result.Add((e, ox, oz));
        });
        return result;
    }

    /// <summary>True se a entity é uma caixa magnética atualmente grudada no player.</summary>
    public static bool IsHeld(GameWorld session, Entity e)
    {
        if (!session.World.Has<Box>(e) || session.World.Get<Box>(e).Type != BoxType.Magnetic)
            return false;

        var player = session.Spatial.First<Player>();
        if (player is null)
            return false;

        var p = session.World.Get<GridPosition>(player.Value);
        var bp = session.World.Get<GridPosition>(e);
        return bp.Y == p.Y && Math.Abs(bp.X - p.X) + Math.Abs(bp.Z - p.Z) == 1;
    }
}
