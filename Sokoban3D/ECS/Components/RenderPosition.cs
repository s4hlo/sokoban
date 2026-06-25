using Microsoft.Xna.Framework;

namespace Sokoban3D.ECS.Components;

/// <summary>
/// Posição visual (no mundo 3D) da entidade, interpolada suavemente em direção
/// à célula lógica do grid. Separa o "onde está desenhado" do "onde está na lógica".
/// </summary>
public struct RenderPosition
{
    public Vector3 Value;

    public RenderPosition(Vector3 value)
    {
        Value = value;
    }
}
