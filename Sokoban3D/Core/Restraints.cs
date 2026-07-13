using Arch.Core;

namespace Sokoban3D.Core;

/// <summary>
/// Ponto único da pergunta "algum efeito de célula segura esta peça contra o passo horizontal
/// (dx,dz)?" — hoje, o grude do obstáculo sticky (<see cref="Stickiness"/>) e o trilho
/// (<see cref="Rails"/>). Toda regra nova de retenção entra AQUI e passa a valer automaticamente
/// pra tudo que consulta retenção: empurrão de caixa, translação/giro do corpo magnético e o
/// desprendimento da caixa magnética que fica pra trás (<see cref="ECS.Systems.MovementSystem"/>).
/// </summary>
public static class Restraints
{
    /// <summary>True se a peça está retida contra o passo horizontal (dx,dz).</summary>
    public static bool Holds(GameWorld world, Entity e, int dx, int dz)
        => Stickiness.Holds(world, e, dx, dz) || Rails.Holds(world, e, dx, dz);
}
