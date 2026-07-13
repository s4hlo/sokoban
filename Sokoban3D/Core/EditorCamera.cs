using System;
using Microsoft.Xna.Framework;

namespace Sokoban3D.Core;

/// <summary>
/// Câmera orbitável exclusiva do modo editor: parte do mesmo enquadramento isométrico da
/// <see cref="Camera"/> do jogo, mas deixa girar (yaw/pitch) e aproximar (zoom) em volta do
/// centro do grid — pra inspecionar e editar células que ficariam oclusas na vista fixa. Não
/// afeta a câmera do jogo (que segue fixa); é só o editor que a usa.
/// </summary>
public class EditorCamera
{
    public Matrix View { get; private set; }
    public Matrix Projection { get; private set; }

    // Estado orbital em torno do alvo: ângulos em radianos e um multiplicador de raio (zoom).
    private float _yaw;
    private float _pitch = DefaultPitch;
    private float _zoom = 1f;

    // Enquadramento base recalculado por grid (raio isométrico e proporção da viewport).
    private float _baseRadius = 1f;
    private float _aspect = 1f;
    private Vector3 _target = Vector3.Zero;

    // Elevação isométrica de origem: a mesma direção (0, 14, 12) da câmera do jogo.
    private static readonly float DefaultPitch = MathF.Atan2(14f, 12f);
    private const float MinPitch = 0.20f; // ~11°: não deita até rasar o horizonte
    private const float MaxPitch = 1.45f; // ~83°: não passa direto por cima (trava o gimbal)
    private const float MinZoom = 0.35f;
    private const float MaxZoom = 3.0f;
    private const float OrbitSpeed = 0.01f;  // radianos por pixel de arrasto
    private const float ZoomStep = 1.1f;     // fator por "tick" de roda

    /// <summary>
    /// Reenquadra pro grid dado e volta à vista isométrica padrão (yaw 0, elevação e zoom de
    /// origem). Chamada ao entrar no editor e a cada redimensionamento — igual à câmera do jogo,
    /// que também reenquadra ao mudar as dimensões.
    /// </summary>
    public void Frame(int gridWidth, int gridDepth, float aspect)
    {
        // Mesmo cálculo de distância da Camera do jogo (8 = tamanho de referência, 0.7 = zoom base).
        float span = Math.Max(gridWidth, gridDepth);
        float distance = Math.Max(1f, span / 8f) * 0.7f;
        _baseRadius = MathF.Sqrt(14f * 14f + 12f * 12f) * distance;
        _aspect = aspect;
        _target = Vector3.Zero;

        _yaw = 0f;
        _pitch = DefaultPitch;
        _zoom = 1f;
        Rebuild();
    }

    /// <summary>Gira a câmera: <paramref name="dxPixels"/> orbita em yaw, <paramref name="dyPixels"/> em pitch.</summary>
    public void Orbit(float dxPixels, float dyPixels)
    {
        _yaw += dxPixels * OrbitSpeed;
        _pitch = Math.Clamp(_pitch - dyPixels * OrbitSpeed, MinPitch, MaxPitch);
        Rebuild();
    }

    /// <summary>Aproxima (<paramref name="ticks"/> &gt; 0) ou afasta a câmera, limitado a [Min,Max]Zoom.</summary>
    public void Zoom(int ticks)
    {
        _zoom = Math.Clamp(_zoom * MathF.Pow(ZoomStep, -ticks), MinZoom, MaxZoom);
        Rebuild();
    }

    private void Rebuild()
    {
        float r = _baseRadius * _zoom;
        // Yaw 0 aponta pra +Z (mesma orientação de origem da câmera do jogo); pitch é a elevação.
        var offset = new Vector3(
            MathF.Cos(_pitch) * MathF.Sin(_yaw),
            MathF.Sin(_pitch),
            MathF.Cos(_pitch) * MathF.Cos(_yaw)) * r;

        View = Matrix.CreateLookAt(_target + offset, _target, Vector3.Up);
        Projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, _aspect, 0.1f, 200f);
    }
}
