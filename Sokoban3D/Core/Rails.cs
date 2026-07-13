using Arch.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Core;

/// <summary>
/// Regra do trilho (<see cref="Rail"/>): a célula com trilho só troca carga pelas PONTAS do tipo
/// (<see cref="RailRules"/>) — a caixa em cima só SAI nas direções das pontas, e caixa de fora só
/// ENTRA chegando por uma ponta (o lado de onde ela vem precisa ser uma saída). Só vale pro
/// movimento de jogo (empurrão, corpo magnético): o undo/restart reposiciona direto pelo History
/// e ignora o trilho, e a queda é vertical. O player não é segurado nem barrado — a restrição é
/// da carga sobre o trilho, não de quem anda. Derivado da célula a cada consulta, nada é
/// armazenado (mesmo espírito de <see cref="Stickiness"/>).
/// </summary>
public static class Rails
{
    /// <summary>
    /// True se a peça está presa por um trilho contra o passo horizontal (dx,dz): alguma célula
    /// do footprint tem um trilho que não permite sair nessa direção.
    /// </summary>
    public static bool Holds(GameWorld world, Entity e, int dx, int dz)
    {
        var pos = world.World.Get<GridPosition>(e);
        foreach (var (ox, oz) in world.FootprintOffsets(e))
            if (RejectsExit(world, pos.X + ox, pos.Y, pos.Z + oz, dx, dz))
                return true;
        return false;
    }

    /// <summary>
    /// True se a célula (x,y,z) tem um trilho que rejeita a SAÍDA de carga pelo passo (dx,dz):
    /// sair é só pelas pontas. Versão por célula do <see cref="Holds"/> — serve pra célula que a
    /// carga só ATRAVESSA no meio de um movimento (a diagonal do giro magnético), quando ela
    /// ainda não está lá pra uma consulta por peça.
    /// </summary>
    public static bool RejectsExit(GameWorld world, int x, int y, int z, int dx, int dz)
    {
        var rail = world.Spatial.CellWith<Rail>(x, y, z);
        return rail is not null && !RailRules.Allows(world.World.Get<Rail>(rail.Value).Type, dx, dz);
    }

    /// <summary>
    /// True se a célula (x,y,z) tem um trilho que rejeita a ENTRADA de carga pelo passo (dx,dz):
    /// quem entra chega pelo lado (-dx,-dz), e esse lado precisa ser uma das pontas do trilho —
    /// o espelho exato da saída, então é o <see cref="RejectsExit"/> com o passo invertido.
    /// Consulta por célula (não por peça): quem sabe quais células a carga vai ocupar é o
    /// movimento que a empurra.
    /// </summary>
    public static bool RejectsEntry(GameWorld world, int x, int y, int z, int dx, int dz)
        => RejectsExit(world, x, y, z, -dx, -dz);
}
