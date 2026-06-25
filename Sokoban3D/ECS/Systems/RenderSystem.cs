using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.ECS.Systems;

/// <summary>
/// Desenha o grid (chão) e as entidades (player, caixas) como cubos 3D.
/// As peças são desenhadas na RenderPosition (visual interpolado), não na célula lógica.
/// </summary>
public class RenderSystem
{
    private static readonly Color FloorColor = new(60, 60, 70);
    private static readonly Color PlayerColor = new(70, 130, 220);

    // Cor por tipo de caixa: leve clara, média intermediária, pesada escura, frágil avermelhada.
    private static readonly Color LightBoxColor = new(210, 180, 140);
    private static readonly Color MediumBoxColor = new(160, 110, 60);
    private static readonly Color HeavyBoxColor = new(90, 60, 40);
    private static readonly Color FragileBoxColor = new(205, 120, 120);

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
                var pos = GridView.ToWorld(grid, x, z, GridView.FloorY);
                _cubes.Draw(pos, scale, FloorColor, view, projection);
            }
    }

    private void DrawEntities(Matrix view, Matrix projection)
    {
        var pieceScale = new Vector3(0.8f);

        var players = new QueryDescription().WithAll<Player, RenderPosition>();
        _world.World.Query(in players, (ref RenderPosition r) =>
        {
            _cubes.Draw(r.Value, pieceScale, PlayerColor, view, projection);
        });

        var boxes = new QueryDescription().WithAll<Box, RenderPosition>();
        _world.World.Query(in boxes, (ref Box b, ref RenderPosition r) =>
        {
            if (b.Broken)
                return; // caixa quebrada não é desenhada

            _cubes.Draw(r.Value, pieceScale, ColorOf(b.Type), view, projection);
        });
    }

    private static Color ColorOf(BoxType type) => type switch
    {
        BoxType.Light => LightBoxColor,
        BoxType.Medium => MediumBoxColor,
        BoxType.Heavy => HeavyBoxColor,
        BoxType.Fragile => FragileBoxColor,
        _ => MediumBoxColor,
    };
}
