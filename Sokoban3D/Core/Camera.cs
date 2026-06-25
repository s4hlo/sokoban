using Microsoft.Xna.Framework;

namespace Sokoban3D.Core;

/// <summary>
/// Câmera fixa em perspectiva, olhando o grid de cima num ângulo isométrico.
/// </summary>
public class Camera
{
    public Matrix View { get; private set; }
    public Matrix Projection { get; private set; }

    public Camera(float aspectRatio, Vector3 target)
    {
        // Posiciona a câmera acima e atrás do alvo, olhando pra baixo num ângulo.
        var position = target + new Vector3(0f, 14f, 12f);

        View = Matrix.CreateLookAt(position, target, Vector3.Up);
        Projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.PiOver4,
            aspectRatio,
            0.1f,
            200f);
    }
}
