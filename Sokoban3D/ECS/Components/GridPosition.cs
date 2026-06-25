namespace Sokoban3D.ECS.Components;

/// <summary>
/// Posição da entity em um grid 3D (x, y, z)
/// </summary>
public struct GridPosition
{
    public int X;
    public int Y;
    public int Z;

    public GridPosition(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public override string ToString() => $"({X}, {Y}, {Z})";
}
