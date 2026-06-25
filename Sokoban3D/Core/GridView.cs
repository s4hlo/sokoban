using Microsoft.Xna.Framework;
using Sokoban3D.Grid;

namespace Sokoban3D.Core;

/// <summary>
/// Converte coordenadas do grid em posições no mundo 3D, centrando o grid na origem em
/// X/Z e usando o Y do grid como altura real. Fonte única dessa conversão pra render e
/// animação não divergirem.
///
/// Convenção de altura: a célula de grid Y ocupa o intervalo de mundo [Y, Y+1]. O
/// parâmetro <c>rise</c> é o deslocamento vertical do centro do que se desenha dentro da
/// célula — escolhido por tipo (peça, obstáculo, marcador) pra cada coisa "encostar" onde
/// faz sentido.
/// </summary>
public static class GridView
{
    /// <summary>Centro de uma peça (escala ~0.8): a base encosta no topo do que está abaixo.</summary>
    public const float PieceRise = 0.4f;

    /// <summary>Centro de um obstáculo: cubo cheio (1×1×1) centrado na célula.</summary>
    public const float ObstacleRise = 0.5f;

    /// <summary>Centro de um marcador (tile plano) pousado no piso da célula.</summary>
    public const float MarkerRise = 0.06f;

    /// <summary>Centro do chão-morte (laje no fundo, na "célula" de grid Y = -1).</summary>
    public const float DeathFloorRise = 0.5f;

    public static Vector3 ToWorld(GridManager grid, int gx, int gy, int gz, float rise)
    {
        float wx = gx - (grid.Width - 1) / 2f;
        float wz = gz - (grid.Depth - 1) / 2f;
        return new Vector3(wx, gy + rise, wz);
    }
}
