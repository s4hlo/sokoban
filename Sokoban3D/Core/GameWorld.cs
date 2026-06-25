using Arch.Core;
using Sokoban3D.Grid;

namespace Sokoban3D.Core;

/// <summary>
/// Gerencia o mundo do jogo: ECS world e grid
/// </summary>
public class GameWorld
{
    public World World { get; }
    public GridManager Grid { get; }

    public GameWorld(int gridWidth, int gridHeight, int gridDepth)
    {
        World = World.Create();
        Grid = new GridManager(gridWidth, gridHeight, gridDepth);
    }

    public void Dispose()
    {
        World.Dispose();
    }
}
