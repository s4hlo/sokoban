using Arch.Core;
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

    public void Dispose()
    {
        World.Dispose();
    }
}
