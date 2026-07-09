using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.ECS.Components;
using Sokoban3D.Grid;
using Sokoban3D.Levels;

namespace Sokoban3D.Core;

/// <summary>
/// Uma sessão de nível: o ECS world, o grid e o histórico de undo de UM nível
/// (ou hub — são a mesma abstração). A navegação entre níveis é uma pilha dessas
/// sessões: entrar num portal empilha uma sessão nova; concluir o objetivo descarta
/// a do topo. Como cada sessão é isolada, o nível-pai fica preservado intacto na
/// pilha — caixas, posições e undo — sem precisar serializar nada.
/// </summary>
public class GameWorld
{
    public World World { get; }
    public GridManager Grid { get; }
    public History History { get; }

    /// <summary>Buscas espaciais ("quem está na célula?") sobre o <see cref="World"/> desta sessão.</summary>
    public SpatialQuery Spatial { get; }

    /// <summary>Receita a partir da qual esta sessão foi montada (usada pelo restart).</summary>
    public Level CurrentLevel { get; set; }

    /// <summary>Identidade do nível, pra rastrear conclusão global. -1 = sem id (default).</summary>
    public int LevelId { get; set; } = -1;

    /// <summary>
    /// True quando o player caiu até o chão-morte e está congelado: o movimento é
    /// ignorado até um Z (undo), R (restart) ou T (suspender) tirá-lo desse estado.
    /// </summary>
    public bool PlayerFell { get; set; }

    public GameWorld(int gridWidth, int gridHeight, int gridDepth)
    {
        World = World.Create();
        Grid = new GridManager(gridWidth, gridHeight, gridDepth);
        History = new History();
        Spatial = new SpatialQuery(World);
    }

    // ----- Mutação espacial (única fonte da invariante grid + GridPosition) -----

    /// <summary>
    /// Move a entity para a célula <paramref name="to"/>: libera a(s) célula(s) atual(is), atualiza
    /// a <see cref="GridPosition"/> (âncora) e ocupa a(s) nova(s). É a única forma de mover algo
    /// mantendo grid e componente em sincronia. Assume jogada legal — o chamador já validou.
    /// </summary>
    public void Move(Entity e, GridPosition to)
    {
        var from = World.Get<GridPosition>(e);
        foreach (var (dx, dz) in FootprintOffsets(e))
            Grid.Vacate(from.X + dx, from.Y, from.Z + dz);
        World.Set(e, to);
        foreach (var (dx, dz) in FootprintOffsets(e))
            Grid.Place(to.X + dx, to.Y, to.Z + dz, e);
    }

    /// <summary>
    /// Ocupa no grid a(s) célula(s) atual(is) da entity (sua <see cref="GridPosition"/> e, se for
    /// uma <see cref="BigBox"/>, a célula extra do footprint). Usado no spawn e ao re-solidificar
    /// uma peça (undo de uma frágil quebrada; restart).
    /// </summary>
    public void Occupy(Entity e)
    {
        var p = World.Get<GridPosition>(e);
        foreach (var (dx, dz) in FootprintOffsets(e))
            Grid.Place(p.X + dx, p.Y, p.Z + dz, e);
    }

    /// <summary>
    /// Libera no grid a(s) célula(s) atual(is) da entity, sem movê-la. Usado ao quebrar uma frágil
    /// (a entity persiste, mas deixa de ocupar o grid).
    /// </summary>
    public void Vacate(Entity e)
    {
        var p = World.Get<GridPosition>(e);
        foreach (var (dx, dz) in FootprintOffsets(e))
            Grid.Vacate(p.X + dx, p.Y, p.Z + dz);
    }

    /// <summary>
    /// Offsets (Dx,Dz) das células que a entity ocupa além da sua <see cref="GridPosition"/>
    /// (que é sempre uma delas, a âncora). Caixa comum: só (0,0). <see cref="BigBox"/>: (0,0) e o
    /// passo do seu <see cref="BigBox.Axis"/>. Fonte única do footprint pra grid, empurrão e queda
    /// nunca divergirem sobre quantas células uma peça ocupa.
    /// </summary>
    public IEnumerable<(int Dx, int Dz)> FootprintOffsets(Entity e)
    {
        yield return (0, 0);
        if (World.Has<BigBox>(e))
        {
            var axis = World.Get<BigBox>(e).Axis;
            yield return axis == BigBoxAxis.X ? (1, 0) : (0, 1);
        }
    }

    /// <summary>
    /// Todas as células do footprint de <paramref name="e"/> em (<paramref name="x"/>,
    /// <paramref name="y"/>, <paramref name="z"/>) [âncora hipotética] estão livres? Assume que a
    /// própria entity NÃO ocupa nenhuma dessas células agora — seguro pra checar outra camada de Y
    /// (queda) ou depois de já ter vacado a entity (undo); NÃO serve pra mover uma BigBox ao longo
    /// do próprio eixo sem vacar antes (a célula traseira ocuparia a mesma que a dianteira libera).
    /// </summary>
    public bool FootprintFree(Entity e, int x, int y, int z)
    {
        foreach (var (dx, dz) in FootprintOffsets(e))
            if (Grid.IsOccupied(x + dx, y, z + dz))
                return false;
        return true;
    }

    public void Dispose()
    {
        World.Dispose();
    }
}
