using Arch.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Core;

/// <summary>
/// Regra do trilho (<see cref="Rail"/>): a caixa em cima de um só sai da célula nas direções que
/// o tipo permite (<see cref="RailRules"/>). Entrar no trilho é livre — ele não ocupa o grid.
/// Só vale pro movimento de jogo (empurrão, corpo magnético): o undo/restart reposiciona direto
/// pelo History e ignora o trilho, e a queda é vertical. O player não é segurado — a restrição é
/// da carga sobre o trilho, não de quem anda. Derivado da célula a cada consulta, nada é
/// armazenado (mesmo espírito de <see cref="Stickiness"/>).
/// </summary>
public static class Rails
{
    /// <summary>
    /// True se a peça está presa por um trilho contra o passo horizontal (dx,dz): alguma célula
    /// do footprint tem um trilho que não permite sair nessa direção.
    /// </summary>
    public static bool Holds(GameWorld world, Entity e, int dx, int dz)
    {
        var pos = world.World.Get<GridPosition>(e);
        foreach (var (ox, oz) in world.FootprintOffsets(e))
        {
            var rail = world.Spatial.CellWith<Rail>(pos.X + ox, pos.Y, pos.Z + oz);
            if (rail is not null && !RailRules.Allows(world.World.Get<Rail>(rail.Value).Type, dx, dz))
                return true;
        }
        return false;
    }
}
