using Microsoft.Xna.Framework;
using Sokoban3D.Grid;

namespace Sokoban3D.Core;

/// <summary>
/// Converte coordenadas do grid em posições no mundo 3D, centrando o grid na origem.
/// Fonte única dessa conversão pra render e animação não divergirem.
/// </summary>
public static class GridView
{
    /// <summary>Altura (Y) das peças (player, caixas) sobre o chão.</summary>
    public const float PieceY = 0.5f;

    /// <summary>Altura (Y) do chão, levemente afundado.</summary>
    public const float FloorY = -0.1f;

    public static Vector3 ToWorld(GridManager grid, int gx, int gz, float y)
    {
        float wx = gx - (grid.Width - 1) / 2f;
        float wz = gz - (grid.Depth - 1) / 2f;
        return new Vector3(wx, y, wz);
    }
}
