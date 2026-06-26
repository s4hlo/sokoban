using Arch.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Core;

/// <summary>
/// Buscas espaciais no ECS de uma sessão: "qual entity está na célula (x,y,z)?". Centraliza
/// num lugar só o que antes cada sistema reimplementava com sua própria query — o lookup
/// espacial é responsabilidade desta classe, não de quem precisa dele. Vive presa ao
/// <see cref="World"/> de uma sessão (uma por GameWorld).
/// </summary>
public sealed class SpatialQuery
{
    private readonly World _world;

    public SpatialQuery(World world)
    {
        _world = world;
    }

    /// <summary>
    /// A entity com o tag <typeparamref name="T"/> na célula (x,y,z), ou null. Para peças
    /// que não ocupam o grid (objetivo, portal) e por isso não saem em <see cref="Occupant"/>.
    /// </summary>
    public Entity? CellWith<T>(int x, int y, int z) where T : struct
    {
        Entity? found = null;
        var query = new QueryDescription().WithAll<T, GridPosition>();
        _world.Query(in query, (Entity e, ref GridPosition p) =>
        {
            if (p.X == x && p.Y == y && p.Z == z)
                found = e;
        });
        return found;
    }

    /// <summary>
    /// A primeira entity com o tag <typeparamref name="T"/>, ou null. Útil para singletons
    /// como o player.
    /// </summary>
    public Entity? First<T>() where T : struct
    {
        Entity? found = null;
        var query = new QueryDescription().WithAll<T>();
        _world.Query(in query, (Entity e) => found = e);
        return found;
    }
}
