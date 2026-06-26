using System.Diagnostics;
using Arch.Core;

namespace Sokoban3D.Grid;

/// <summary>
/// Gerencia o grid 3D: dimensões, ocupação e validações. A ocupação é um índice espacial
/// célula → entity: cada célula guarda a peça que a ocupa (ou null). É a fonte de verdade
/// sobre "quem está aqui?" — uma célula guarda no máximo uma entity (invariante "um
/// <c>Solid</c> por célula"), garantida pela própria estrutura. Continua puro (não conhece
/// o <see cref="World"/>): a sincronia com a <c>GridPosition</c> é responsabilidade da
/// <see cref="Sokoban3D.Core.GameWorld"/>.
/// </summary>
public class GridManager
{
    private int _width;
    private int _height;
    private int _depth;

    // Mapa de ocupação: a entity que ocupa cada célula, ou null se livre.
    private Entity?[,,] _occupant;

    public GridManager(int width, int height, int depth)
    {
        _width = width;
        _height = height;
        _depth = depth;
        _occupant = new Entity?[width, height, depth];
    }

    /// <summary>
    /// Redimensiona o grid para as dimensões de um novo nível, realocando o mapa de
    /// ocupação zerado. Cada nível tem seu próprio tamanho.
    /// </summary>
    public void Resize(int width, int height, int depth)
    {
        _width = width;
        _height = height;
        _depth = depth;
        _occupant = new Entity?[width, height, depth];
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
    /// Verifica se posição está ocupada. Fora do grid conta como ocupado (a borda é parede).
    /// </summary>
    public bool IsOccupied(int x, int y, int z)
    {
        if (!IsValid(x, y, z))
            return true; // Fora do grid = ocupado

        return _occupant[x, y, z] is not null;
    }

    /// <summary>
    /// A entity que ocupa a célula (x,y,z), ou null se está livre (ou fora dos limites). O(1).
    /// </summary>
    public Entity? Occupant(int x, int y, int z)
    {
        if (!IsValid(x, y, z))
            return null;

        return _occupant[x, y, z];
    }

    /// <summary>
    /// Marca a célula como ocupada por <paramref name="e"/>. Em debug, acusa se a célula já
    /// está ocupada por outra entity (sinal de dessincronia entre grid e GridPosition).
    /// </summary>
    public void Place(int x, int y, int z, Entity e)
    {
        if (!IsValid(x, y, z))
            return;

        var current = _occupant[x, y, z];
        Debug.Assert(current is null || current.Value == e,
            $"Place numa célula já ocupada por outra entity em ({x},{y},{z})");
        _occupant[x, y, z] = e;
    }

    /// <summary>
    /// Libera a célula (x,y,z).
    /// </summary>
    public void Vacate(int x, int y, int z)
    {
        if (IsValid(x, y, z))
            _occupant[x, y, z] = null;
    }

    /// <summary>
    /// Limpa o grid (todas as células livres).
    /// </summary>
    public void Clear()
    {
        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
                for (int z = 0; z < _depth; z++)
                    _occupant[x, y, z] = null;
    }

    public int Width => _width;
    public int Height => _height;
    public int Depth => _depth;
}
