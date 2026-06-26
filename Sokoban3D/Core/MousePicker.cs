using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban3D.Grid;

namespace Sokoban3D.Core;

/// <summary>
/// Converte a posição 2D do mouse na célula 3D sob o ponteiro. Como a câmera é fixa, lança um
/// raio do ponteiro (via <see cref="Viewport.Unproject"/>) e o intersecta com o plano horizontal
/// no centro de uma camada Y do grid — o resto é o inverso de <see cref="GridView.ToWorld"/>.
/// Fonte única dessa conversão tela → grid pro editor.
/// </summary>
public static class MousePicker
{
    /// <summary>
    /// Célula (X,Z) da camada <paramref name="layerY"/> sob o ponteiro, ou null se o raio não
    /// cruza essa camada dentro dos limites do grid (ponteiro fora do tabuleiro, atrás da câmera
    /// ou paralelo ao plano).
    /// </summary>
    public static (int X, int Z)? PickCell(
        Viewport viewport, Matrix view, Matrix projection, int mouseX, int mouseY,
        GridManager grid, int layerY)
    {
        // Dois pontos do raio: o ponteiro no plano próximo (z=0) e no distante (z=1) da tela.
        var near = viewport.Unproject(new Vector3(mouseX, mouseY, 0f), projection, view, Matrix.Identity);
        var far = viewport.Unproject(new Vector3(mouseX, mouseY, 1f), projection, view, Matrix.Identity);
        var dir = far - near;

        // Plano no centro da camada (a célula Y ocupa [Y, Y+1]; o cursor wireframe fica em Y+0.5).
        float planeY = layerY + 0.5f;
        if (Math.Abs(dir.Y) < 1e-6f)
            return null; // raio paralelo ao plano

        float t = (planeY - near.Y) / dir.Y;
        if (t < 0f)
            return null; // plano atrás da câmera

        var hit = near + dir * t;
        var (fx, fz) = GridView.ToGridXZ(grid, hit.X, hit.Z);
        int gx = (int)MathF.Round(fx);
        int gz = (int)MathF.Round(fz);

        if (gx < 0 || gx >= grid.Width || gz < 0 || gz >= grid.Depth)
            return null;

        return (gx, gz);
    }
}
