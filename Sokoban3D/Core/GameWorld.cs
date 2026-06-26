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
    /// Move a entity para a célula <paramref name="to"/>: libera a célula atual, atualiza a
    /// <see cref="GridPosition"/> e ocupa a nova. É a única forma de mover algo mantendo grid
    /// e componente em sincronia. Assume jogada legal — o chamador já validou.
    /// </summary>
    public void Move(Entity e, GridPosition to)
    {
        var from = World.Get<GridPosition>(e);
        Grid.Vacate(from.X, from.Y, from.Z);
        World.Set(e, to);
        Grid.Place(to.X, to.Y, to.Z, e);
    }

    /// <summary>
    /// Ocupa no grid a célula atual da entity (sua <see cref="GridPosition"/>). Usado no spawn
    /// e ao re-solidificar uma peça (undo de uma frágil quebrada; restart).
    /// </summary>
    public void Occupy(Entity e)
    {
        var p = World.Get<GridPosition>(e);
        Grid.Place(p.X, p.Y, p.Z, e);
    }

    /// <summary>
    /// Libera no grid a célula atual da entity, sem movê-la. Usado ao quebrar uma frágil
    /// (a entity persiste, mas deixa de ocupar o grid).
    /// </summary>
    public void Vacate(Entity e)
    {
        var p = World.Get<GridPosition>(e);
        Grid.Vacate(p.X, p.Y, p.Z);
    }

    public void Dispose()
    {
        World.Dispose();
    }
}
