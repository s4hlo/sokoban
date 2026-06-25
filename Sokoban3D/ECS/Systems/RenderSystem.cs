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
    // Chão-morte: laje escura no fundo, visível pelos buracos. Pisar nela mata.
    private static readonly Color DeathFloorColor = new(35, 20, 25);
    // Obstáculos (terreno sólido onde se anda).
    private static readonly Color ObstacleColor = new(95, 100, 115);
    private static readonly Color PlayerColor = new(70, 130, 220);
    // Player caído (congelado no chão-morte): avermelhado pra sinalizar a morte.
    private static readonly Color PlayerFellColor = new(200, 70, 70);

    // Meta do nível (dourado) e portais para níveis filhos (ciano; verde quando concluído).
    private static readonly Color ObjectiveColor = new(235, 200, 70);
    private static readonly Color PortalColor = new(70, 200, 210);
    private static readonly Color PortalDoneColor = new(70, 200, 110);

    // Cor por tipo de caixa: leve clara, média intermediária, pesada escura, frágil avermelhada.
    private static readonly Color LightBoxColor = new(210, 180, 140);
    private static readonly Color MediumBoxColor = new(160, 110, 60);
    private static readonly Color HeavyBoxColor = new(90, 60, 40);
    private static readonly Color FragileBoxColor = new(205, 120, 120);
    private static readonly Color PermanentBoxColor = new(60, 170, 75);

    // Sessão ativa do frame. O CubeRenderer (preso ao device) é reutilizado entre sessões.
    private GameWorld _world;
    private readonly CubeRenderer _cubes;

    public RenderSystem(GraphicsDevice device)
    {
        _cubes = new CubeRenderer(device);
    }

    public void Draw(GameWorld session, Matrix view, Matrix projection)
    {
        _world = session;
        DrawDeathFloor(view, projection);
        DrawObstacles(view, projection);
        DrawMarkers(view, projection);
        DrawEntities(view, projection);
    }

    /// <summary>
    /// Desenha marcadores que não se movem nem ocupam o grid — objetivos e portais —
    /// como tiles planos sobre o chão, na célula lógica (sem interpolação).
    /// </summary>
    private void DrawMarkers(Matrix view, Matrix projection)
    {
        var grid = _world.Grid;
        var tileScale = new Vector3(0.7f, 0.1f, 0.7f);

        var objectives = new QueryDescription().WithAll<Objective, GridPosition>();
        _world.World.Query(in objectives, (ref GridPosition p) =>
        {
            var pos = GridView.ToWorld(grid, p.X, p.Y, p.Z, GridView.MarkerRise);
            _cubes.Draw(pos, tileScale, ObjectiveColor, view, projection);
        });

        var portals = new QueryDescription().WithAll<LevelPortal, GridPosition>();
        _world.World.Query(in portals, (ref LevelPortal portal, ref GridPosition p) =>
        {
            var pos = GridView.ToWorld(grid, p.X, p.Y, p.Z, GridView.MarkerRise);
            var color = portal.Completed ? PortalDoneColor : PortalColor;
            _cubes.Draw(pos, tileScale, color, view, projection);
        });
    }

    /// <summary>
    /// Chão-morte: a laje sólida do fundo (na "célula" de grid Y = -1), abaixo do terreno.
    /// Aparece pelos buracos; o player que pousa direto nela morre.
    /// </summary>
    private void DrawDeathFloor(Matrix view, Matrix projection)
    {
        var grid = _world.Grid;
        // Laje fina e plana, claramente abaixo da base do terreno (que começa em y=0 de mundo).
        var scale = new Vector3(1f, 0.2f, 1f);

        for (int x = 0; x < grid.Width; x++)
            for (int z = 0; z < grid.Depth; z++)
            {
                var pos = GridView.ToWorld(grid, x, -1, z, GridView.DeathFloorRise);
                _cubes.Draw(pos, scale, DeathFloorColor, view, projection);
            }
    }

    /// <summary>
    /// Obstáculos: o terreno sólido. Cubos cheios na altura, levemente encolhidos em X/Z pra
    /// aparecer a emenda entre blocos (lê melhor o relevo); a altura fica cheia pra os topos
    /// alinharem com a base das peças e os empilhamentos ficarem coesos.
    /// </summary>
    private void DrawObstacles(Matrix view, Matrix projection)
    {
        var grid = _world.Grid;
        var scale = new Vector3(0.96f, 1f, 0.96f);

        var query = new QueryDescription().WithAll<Obstacle, GridPosition>();
        _world.World.Query(in query, (ref GridPosition p) =>
        {
            var pos = GridView.ToWorld(grid, p.X, p.Y, p.Z, GridView.ObstacleRise);
            _cubes.Draw(pos, scale, ObstacleColor, view, projection);
        });
    }

    private void DrawEntities(Matrix view, Matrix projection)
    {
        var pieceScale = new Vector3(0.8f);

        var playerColor = _world.PlayerFell ? PlayerFellColor : PlayerColor;
        var players = new QueryDescription().WithAll<Player, RenderPosition>();
        _world.World.Query(in players, (ref RenderPosition r) =>
        {
            _cubes.Draw(r.Value, pieceScale, playerColor, view, projection);
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
        BoxType.Permanent => PermanentBoxColor,
        _ => MediumBoxColor,
    };
}
