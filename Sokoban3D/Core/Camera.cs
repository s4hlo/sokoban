using System;
using Microsoft.Xna.Framework;

namespace Sokoban3D.Core;

/// <summary>
/// Câmera fixa em perspectiva, olhando o grid de cima num ângulo isométrico.
/// O afastamento escala com o tamanho do grid pra enquadrar qualquer nível (a raiz é maior).
/// </summary>
public class Camera
{
    public Matrix View { get; private set; }
    public Matrix Projection { get; private set; }

    // Fator de aproximação do enquadramento padrão: 1 = o afastamento original; menor = mais
    // perto. Escala a posição inteira, então o ângulo isométrico não muda.
    private const float Zoom = 0.7f;

    public Camera(float aspectRatio, int gridWidth, int gridDepth)
    {
        // Distância proporcional ao maior lado do grid (8 = tamanho de referência).
        float span = Math.Max(gridWidth, gridDepth);
        float distance = Math.Max(1f, span / 8f) * Zoom;

        var target = Vector3.Zero;
        var position = target + new Vector3(0f, 14f * distance, 12f * distance);

        View = Matrix.CreateLookAt(position, target, Vector3.Up);
        Projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.PiOver4,
            aspectRatio,
            0.1f,
            200f);
    }
}
