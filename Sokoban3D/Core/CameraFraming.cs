using System;
using Microsoft.Xna.Framework;

namespace Sokoban3D.Core;

/// <summary>
/// Enquadramento isométrico compartilhado pelas duas câmeras: a <see cref="Camera"/> fixa do jogo
/// e a <see cref="EditorCamera"/> orbitável. Ambas partem da MESMA direção de origem (altura x
/// profundidade) e da mesma regra de distância por grid — este é o único lugar que define esses
/// números, pra as duas nunca desincronizarem (mudar o ângulo aqui muda o jogo e o editor juntos).
/// </summary>
public static class CameraFraming
{
    // Direção de origem do olhar: a câmera fica <see cref="Height"/> acima e <see cref="Depth"/>
    // atrás (em Z) do alvo, sem deslocamento em X. A razão altura/profundidade É o ângulo de
    // elevação (atan(Height/Depth)); subir Height deixa mais "de cima".
    public const float Height = 20f;
    public const float Depth = 12f;

    // Distância proporcional ao maior lado do grid (ReferenceSpan = tamanho de referência) vezes
    // um zoom base (< 1 = mais perto). Enquadra qualquer nível sem mudar o ângulo.
    private const float ReferenceSpan = 8f;
    private const float BaseZoom = 0.5f;

    // Perspectiva compartilhada.
    public const float FieldOfView = MathHelper.PiOver4;
    public const float NearPlane = 0.1f;
    public const float FarPlane = 200f;

    /// <summary>Elevação isométrica de origem, em radianos (atan(Height/Depth)).</summary>
    public static float DefaultPitch => MathF.Atan2(Height, Depth);

    /// <summary>Fator de afastamento pro grid dado — mesma regra pras duas câmeras.</summary>
    public static float Distance(int gridWidth, int gridDepth)
    {
        float span = Math.Max(gridWidth, gridDepth);
        return Math.Max(1f, span / ReferenceSpan) * BaseZoom;
    }

    /// <summary>Raio da órbita na vista de origem: o comprimento do vetor (Height, Depth) escalado.</summary>
    public static float Radius(int gridWidth, int gridDepth)
        => MathF.Sqrt(Height * Height + Depth * Depth) * Distance(gridWidth, gridDepth);
}
