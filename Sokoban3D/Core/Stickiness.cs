using Arch.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Core;

/// <summary>
/// Regra do obstáculo grudento (<see cref="ObstacleType.Sticky"/>): quem está adjacente a um
/// não consegue se mover na direção OPOSTA a ele (se afastar) — o grude segura. Direções
/// perpendiculares seguem livres, e entrar nele já é bloqueado pela solidez normal. Derivado
/// da adjacência a cada consulta, nada é armazenado (mesmo espírito de <see cref="Magnetism"/>).
/// </summary>
public static class Stickiness
{
    /// <summary>
    /// True se a peça está grudada contra o passo horizontal (dx,dz): existe um obstáculo
    /// sticky na célula logo ATRÁS do movimento (aquela da qual a peça estaria se afastando),
    /// pra qualquer célula do footprint.
    /// </summary>
    public static bool Holds(GameWorld world, Entity e, int dx, int dz)
    {
        var pos = world.World.Get<GridPosition>(e);
        foreach (var (ox, oz) in world.FootprintOffsets(e))
            if (IsSticky(world, pos.X + ox - dx, pos.Y, pos.Z + oz - dz))
                return true;
        return false;
    }

    /// <summary>
    /// True se a peça está pendurada num sticky logo ACIMA: cair seria se afastar dele, então
    /// o grude a segura contra a gravidade (mesma regra dos lados, aplicada ao eixo Y).
    /// </summary>
    public static bool HoldsAgainstFall(GameWorld world, Entity e)
    {
        var pos = world.World.Get<GridPosition>(e);
        foreach (var (ox, oz) in world.FootprintOffsets(e))
            if (IsSticky(world, pos.X + ox, pos.Y + 1, pos.Z + oz))
                return true;
        return false;
    }

    private static bool IsSticky(GameWorld world, int x, int y, int z)
        => world.Grid.Occupant(x, y, z) is { } e
        && world.World.Has<Obstacle>(e)
        && world.World.Get<Obstacle>(e).Type == ObstacleType.Sticky;
}
