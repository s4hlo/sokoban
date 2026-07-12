using Arch.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Core;

/// <summary>
/// Estado da meta condicionada a coletáveis: existindo pelo menos uma caixa
/// <see cref="BoxType.Collectible"/> ainda sólida (não coletada), o objetivo fica desabilitado —
/// pisar nele não conclui o nível (ver <see cref="Sokoban3D.Game1"/> e <see cref="Solver.SolverSim"/>). Sem
/// coletável nenhum no nível, a condição é vacuamente satisfeita e a meta funciona como sempre.
/// Nada é armazenado: "coletado" é só a caixa ter perdido o <see cref="Solid"/> (ver
/// <see cref="Systems.MovementSystem"/>), então o undo devolve o estado de graça igual à frágil.
/// </summary>
public static class Collectibles
{
    public static bool AllCollected(GameWorld session)
    {
        bool pending = false;
        var query = new QueryDescription().WithAll<Box, Solid>();
        session.World.Query(in query, (ref Box b) =>
        {
            if (b.Type == BoxType.Collectible)
                pending = true;
        });
        return !pending;
    }
}
