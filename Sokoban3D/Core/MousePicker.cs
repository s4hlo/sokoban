using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban3D.Grid;

namespace Sokoban3D.Core;

/// <summary>
/// Resultado do picking 3D do editor: <see cref="Hit"/> é a célula sólida que o raio do
/// ponteiro atingiu (ou a célula do plano da camada, no fallback) e <see cref="Place"/> é a
/// célula encostada na face atingida — onde uma peça nova seria colocada, sempre vazia quando
/// veio de um raycast. Place é null quando a face aponta pra fora do grid.
/// </summary>
public readonly record struct VoxelPick((int X, int Y, int Z) Hit, (int X, int Y, int Z)? Place);

/// <summary>
/// Converte a posição 2D do mouse em células 3D do grid. Dois modos: <see cref="PickVoxel"/>
/// lança um raio contra os blocos ocupados da cena (o clique acerta o que se vê, como num
/// editor de voxels); <see cref="PickCell"/> intersecta um plano horizontal numa camada Y fixa
/// (usado como fallback no ar e pra travar a camada durante arrastos). Fonte única da
/// conversão tela → grid pro editor.
/// </summary>
public static class MousePicker
{
    /// <summary>
    /// Picking contra os blocos da cena: caminha o raio célula a célula (DDA de Amanatides
    /// &amp; Woo) até a primeira ocupada e devolve essa célula mais a adjacente à face
    /// atingida. Se o raio não acerta nada, cai no plano da camada
    /// <paramref name="fallbackLayerY"/> (Hit == Place), pra ainda dar pra construir no ar.
    /// </summary>
    public static VoxelPick? PickVoxel(
        Viewport viewport, Matrix view, Matrix projection, int mouseX, int mouseY,
        GridManager grid, int fallbackLayerY)
    {
        var near = viewport.Unproject(new Vector3(mouseX, mouseY, 0f), projection, view, Matrix.Identity);
        var far = viewport.Unproject(new Vector3(mouseX, mouseY, 1f), projection, view, Matrix.Identity);
        var dir = far - near;

        if (RayCastSolid(near, dir, grid, out var hit, out var step))
        {
            // A célula de colocação é a vizinha pela face de entrada (o raio veio dela, então
            // ela é garantidamente vazia). Fora do grid não há onde colocar.
            var place = (X: hit.X - step.X, Y: hit.Y - step.Y, Z: hit.Z - step.Z);
            return new VoxelPick(hit, grid.IsValid(place.X, place.Y, place.Z) ? place : null);
        }

        if (PickCell(viewport, view, projection, mouseX, mouseY, grid, fallbackLayerY) is not { } cell)
            return null;

        var flat = (cell.X, fallbackLayerY, cell.Z);
        return new VoxelPick(flat, flat);
    }

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

    /// <summary>
    /// DDA de voxels: avança o raio célula a célula dentro do grid até a primeira ocupada.
    /// Devolve a célula atingida e o passo (±1 num eixo) com que o raio ENTROU nela — a
    /// negação do passo é a normal da face, usada pra achar a célula de colocação.
    /// </summary>
    private static bool RayCastSolid(
        Vector3 worldOrigin, Vector3 dir, GridManager grid,
        out (int X, int Y, int Z) hit, out (int X, int Y, int Z) enterStep)
    {
        hit = default;
        enterStep = default;

        // Espaço de grid: a célula (i,j,k) ocupa [i,i+1)³ (inverso de GridView.ToWorld, com
        // a meia-célula somada pra alinhar o canto — o Y do mundo já é o Y do grid).
        var o = new Vector3(
            worldOrigin.X + (grid.Width - 1) / 2f + 0.5f,
            worldOrigin.Y,
            worldOrigin.Z + (grid.Depth - 1) / 2f + 0.5f);

        if (!ClipToBox(o, dir, grid.Width, grid.Height, grid.Depth, out float tEnter, out int enterAxis))
            return false;

        // Ponto de partida: a entrada na caixa do grid, com um empurrãozinho pra dentro pra o
        // floor não cair na célula errada quando o ponto está exatamente na fronteira.
        float t = MathF.Max(tEnter, 0f) + 1e-4f;
        var p = o + dir * t;
        int x = Math.Clamp((int)MathF.Floor(p.X), 0, grid.Width - 1);
        int y = Math.Clamp((int)MathF.Floor(p.Y), 0, grid.Height - 1);
        int z = Math.Clamp((int)MathF.Floor(p.Z), 0, grid.Depth - 1);

        int stepX = Math.Sign(dir.X), stepY = Math.Sign(dir.Y), stepZ = Math.Sign(dir.Z);
        float tMaxX = NextCrossing(o.X, dir.X, x);
        float tMaxY = NextCrossing(o.Y, dir.Y, y);
        float tMaxZ = NextCrossing(o.Z, dir.Z, z);
        float tDeltaX = dir.X == 0f ? float.PositiveInfinity : MathF.Abs(1f / dir.X);
        float tDeltaY = dir.Y == 0f ? float.PositiveInfinity : MathF.Abs(1f / dir.Y);
        float tDeltaZ = dir.Z == 0f ? float.PositiveInfinity : MathF.Abs(1f / dir.Z);

        int axis = enterAxis;
        while (true)
        {
            // Dentro do loop (x,y,z) está sempre nos limites, então ocupado = bloco de verdade.
            if (grid.IsOccupied(x, y, z))
            {
                hit = (x, y, z);
                enterStep = axis switch
                {
                    0 => (stepX, 0, 0),
                    1 => (0, stepY, 0),
                    _ => (0, 0, stepZ),
                };
                return true;
            }

            // Avança pra próxima célula pelo eixo cuja fronteira está mais perto.
            if (tMaxX <= tMaxY && tMaxX <= tMaxZ) { x += stepX; tMaxX += tDeltaX; axis = 0; }
            else if (tMaxY <= tMaxZ) { y += stepY; tMaxY += tDeltaY; axis = 1; }
            else { z += stepZ; tMaxZ += tDeltaZ; axis = 2; }

            if (!grid.IsValid(x, y, z))
                return false; // saiu do grid sem acertar nada
        }
    }

    /// <summary>t em que o raio cruza a próxima fronteira de célula no eixo (o e d do eixo).</summary>
    private static float NextCrossing(float o, float d, int cell)
    {
        if (d == 0f)
            return float.PositiveInfinity;
        float boundary = d > 0f ? cell + 1 : cell;
        return (boundary - o) / d;
    }

    /// <summary>
    /// Interseção raio × caixa do grid [0,W]×[0,H]×[0,D] (slabs). Devolve o t de entrada e o
    /// eixo da face por onde entrou (pra normal do primeiro hit).
    /// </summary>
    private static bool ClipToBox(
        Vector3 o, Vector3 d, float w, float h, float depth, out float tEnter, out int enterAxis)
    {
        tEnter = float.MinValue;
        float tExit = float.MaxValue;
        enterAxis = 1; // default: face de cima (raio nascido dentro da caixa)

        return ClipSlab(o.X, d.X, w, 0, ref tEnter, ref tExit, ref enterAxis)
            && ClipSlab(o.Y, d.Y, h, 1, ref tEnter, ref tExit, ref enterAxis)
            && ClipSlab(o.Z, d.Z, depth, 2, ref tEnter, ref tExit, ref enterAxis);
    }

    private static bool ClipSlab(
        float o, float d, float max, int axis, ref float tEnter, ref float tExit, ref int enterAxis)
    {
        if (MathF.Abs(d) < 1e-8f)
            return o >= 0f && o <= max; // paralelo ao slab: dentro ou fora pra sempre

        float t0 = -o / d;
        float t1 = (max - o) / d;
        if (t0 > t1)
            (t0, t1) = (t1, t0);

        if (t0 > tEnter)
        {
            tEnter = t0;
            enterAxis = axis;
        }
        if (t1 < tExit)
            tExit = t1;
        return tEnter <= tExit;
    }
}
