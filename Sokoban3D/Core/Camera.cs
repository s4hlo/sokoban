using Microsoft.Xna.Framework;

namespace Sokoban3D.Core;

/// <summary>
/// Câmera fixa em perspectiva, olhando o grid de cima num ângulo isométrico. O enquadramento
/// (direção do olhar, distância por grid, FOV) vem todo do <see cref="CameraFraming"/>, o mesmo
/// que a <see cref="EditorCamera"/> usa — as duas nunca desincronizam.
/// </summary>
public class Camera
{
    public Matrix View { get; private set; }
    public Matrix Projection { get; private set; }

    public Camera(float aspectRatio, int gridWidth, int gridDepth)
    {
        float distance = CameraFraming.Distance(gridWidth, gridDepth);

        var target = Vector3.Zero;
        var position = target + new Vector3(0f, CameraFraming.Height * distance, CameraFraming.Depth * distance);

        View = Matrix.CreateLookAt(position, target, Vector3.Up);
        Projection = Matrix.CreatePerspectiveFieldOfView(
            CameraFraming.FieldOfView,
            aspectRatio,
            CameraFraming.NearPlane,
            CameraFraming.FarPlane);
    }
}
