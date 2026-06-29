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

/// <summary>
/// Animação transitória de teleporte: enquanto presente, o <see cref="RenderPosition"/> da peça
/// é dirigido em duas fases — desliza de <see cref="Start"/> pra dentro do portal de entrada
/// (<see cref="Entry"/>), some, e brota do portal de saída (<see cref="Exit"/>) deslizando até a
/// célula final (a posição do grid). Removida quando <see cref="Elapsed"/> chega ao fim, devolvendo
/// a peça à interpolação normal. Só a põe quem atravessa um portal (<see cref="Systems.MovementSystem"/>).
/// </summary>
public struct TeleportAnim
{
    public Vector3 Start;
    public Vector3 Entry;
    public Vector3 Exit;
    public float Elapsed;
}
