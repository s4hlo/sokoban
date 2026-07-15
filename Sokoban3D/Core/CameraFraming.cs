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

    // Distância proporcional ao lado dominante do grid (ver EffectiveSpan) vezes um zoom base
    // (< 1 = mais perto). Enquadra qualquer nível sem mudar o ângulo.
    private const float ReferenceSpan = 6f;
    private const float BaseZoom = 0.45f;

    // Peso da largura vs profundidade no enquadramento. Numa tela 16:9 o FOV horizontal (~73°) é
    // bem maior que o vertical (45°), então a largura (X, eixo horizontal) "cabe" mais fácil que a
    // profundidade (Z, eixo vertical). Contamos a largura com peso < 1 pra um nível fundo afastar
    // mais que um igualmente largo. ~0.56 ≈ tan(FOV_v/2)/tan(FOV_h/2) no 16:9 (assume essa razão;
    // se um dia rodar em outro aspect, derivar daqui em vez do valor fixo).
    private const float WidthWeight = 0.56f;

    // Perspectiva compartilhada.
    public const float FieldOfView = MathHelper.PiOver4;
    public const float NearPlane = 0.1f;
    public const float FarPlane = 200f;

    /// <summary>Elevação isométrica de origem, em radianos (atan(Height/Depth)).</summary>
    public static float DefaultPitch => MathF.Atan2(Height, Depth);

    /// <summary>Fator de afastamento pro grid dado — mesma regra pras duas câmeras.</summary>
    public static float Distance(int gridWidth, int gridDepth)
    {
        // Largura descontada pelo WidthWeight (16:9 dá folga horizontal): nível fundo afasta mais.
        float span = Math.Max(gridWidth * WidthWeight, gridDepth);
        return Math.Max(1f, span / ReferenceSpan) * BaseZoom;
    }

    /// <summary>Raio da órbita na vista de origem: o comprimento do vetor (Height, Depth) escalado.</summary>
    public static float Radius(int gridWidth, int gridDepth)
        => MathF.Sqrt(Height * Height + Depth * Depth) * Distance(gridWidth, gridDepth);
}
