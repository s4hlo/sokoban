namespace Sokoban3D.Grid;

/// <summary>
/// Gerencia o grid 3D: dimensões, ocupação, validações
/// </summary>
public class GridManager
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _depth;

    // Mapa de ocupação: true = espaço ocupado
    private readonly bool[,,] _occupied;

    public GridManager(int width, int height, int depth)
    {
        _width = width;
        _height = height;
        _depth = depth;
        _occupied = new bool[width, height, depth];
    }

    /// <summary>
    /// Verifica se posição é válida no grid
    /// </summary>
    public bool IsValid(int x, int y, int z)
    {
        return x >= 0 && x < _width &&
               y >= 0 && y < _height &&
               z >= 0 && z < _depth;
    }

    /// <summary>
    /// Verifica se posição está ocupada
    /// </summary>
    public bool IsOccupied(int x, int y, int z)
    {
        if (!IsValid(x, y, z))
            return true; // Fora do grid = ocupado

        return _occupied[x, y, z];
    }

    /// <summary>
    /// Marca posição como ocupada
    /// </summary>
    public void SetOccupied(int x, int y, int z, bool occupied)
    {
        if (IsValid(x, y, z))
            _occupied[x, y, z] = occupied;
    }

    /// <summary>
    /// Limpa o grid
    /// </summary>
    public void Clear()
    {
        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
                for (int z = 0; z < _depth; z++)
                    _occupied[x, y, z] = false;
    }

    public int Width => _width;
    public int Height => _height;
    public int Depth => _depth;
}
