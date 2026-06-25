using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.ECS.Systems;

/// <summary>
/// Desenha o grid (chão) e as entidades (player, caixas) como cubos 3D.
/// Gameplay é no plano X/Z (Y é só altura visual).
/// </summary>
public class RenderSystem
{
    private static readonly Color FloorColor = new(60, 60, 70);
    private static readonly Color PlayerColor = new(70, 130, 220);
    private static readonly Color BoxColor = new(160, 110, 60);

    private readonly GameWorld _world;
    private readonly CubeRenderer _cubes;

    public RenderSystem(GameWorld world, GraphicsDevice device)
    {
        _world = world;
        _cubes = new CubeRenderer(device);
    }

    public void Draw(Matrix view, Matrix projection)
    {
        DrawFloor(view, projection);
        DrawEntities(view, projection);
    }

    private void DrawFloor(Matrix view, Matrix projection)
    {
        var grid = _world.Grid;
        var scale = new Vector3(0.95f, 0.2f, 0.95f);

        for (int x = 0; x < grid.Width; x++)
            for (int z = 0; z < grid.Depth; z++)
            {
                var pos = GridToWorld(x, z, 0f);
                pos.Y = -0.1f; // afunda o chão um pouco abaixo das peças
                _cubes.Draw(pos, scale, FloorColor, view, projection);
            }
    }

    private void DrawEntities(Matrix view, Matrix projection)
    {
        var pieceScale = new Vector3(0.8f);

        var players = new QueryDescription().WithAll<Player, GridPosition>();
        _world.World.Query(in players, (ref GridPosition p) =>
        {
            _cubes.Draw(GridToWorld(p.X, p.Z, 0.5f), pieceScale, PlayerColor, view, projection);
        });

        var boxes = new QueryDescription().WithAll<Box, GridPosition>();
        _world.World.Query(in boxes, (ref GridPosition p) =>
        {
            _cubes.Draw(GridToWorld(p.X, p.Z, 0.5f), pieceScale, BoxColor, view, projection);
        });
    }

    /// <summary>
    /// Converte uma célula do grid (x, z) em posição no mundo, centrando o grid na origem.
    /// </summary>
    private Vector3 GridToWorld(int gx, int gz, float y)
    {
        var grid = _world.Grid;
        float wx = gx - (grid.Width - 1) / 2f;
        float wz = gz - (grid.Depth - 1) / 2f;
        return new Vector3(wx, y, wz);
    }
}
