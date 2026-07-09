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
    // "Nariz" do player: cubinho claro na face pra onde ele olha (essencial com caixa magnética
    // grudada, quando o olhar decide se um comando anda ou gira).
    private static readonly Color PlayerNoseColor = new(230, 238, 250);

    // Meta do nível (dourado) e portais para níveis filhos (ciano; verde quando concluído).
    private static readonly Color ObjectiveColor = new(235, 200, 70);
    private static readonly Color PortalColor = new(70, 200, 210);
    private static readonly Color PortalDoneColor = new(70, 200, 110);

    // Placa de pressão (tile plano): clara em repouso, acende quando há peça em cima.
    private static readonly Color PlateColor = new(150, 110, 200);
    private static readonly Color PlatePressedColor = new(210, 170, 255);
    // Base atemporal (tile plano verde): congela o undo de quem está em cima.
    private static readonly Color TimelessBaseColor = new(60, 200, 120);
    // Bloco toggle: cubo roxo quando sólido; tile fino (pegada) quando aberto, pra anteceder
    // onde ele vai aparecer.
    private static readonly Color ToggleSolidColor = new(130, 80, 190);
    private static readonly Color ToggleOpenColor = new(80, 55, 110);

    // Cor por tipo de caixa: leve clara, média intermediária, pesada escura, frágil avermelhada.
    private static readonly Color LightBoxColor = new(210, 180, 140);
    private static readonly Color MediumBoxColor = new(160, 110, 60);
    private static readonly Color HeavyBoxColor = new(90, 60, 40);
    private static readonly Color FragileBoxColor = new(205, 120, 120);
    private static readonly Color PermanentBoxColor = new(60, 170, 75);
    // Caixa portal: magenta vivo, pra não se confundir com o tile ciano dos portais de nível.
    private static readonly Color PortalBoxColor = new(200, 70, 200);
    // Caixa magnética: aço azulado, meio-termo entre as caixas de madeira e o player azul.
    private static readonly Color MagneticBoxColor = new(140, 160, 185);

    // Sessão ativa do frame. O CubeRenderer (preso ao device) é reutilizado entre sessões
    // e compartilhado com o editor — por isso vem injetado, não criado aqui.
    private GameWorld _world;
    private readonly CubeRenderer _cubes;

    public RenderSystem(CubeRenderer cubes)
    {
        _cubes = cubes;
    }

    public void Draw(GameWorld session, Matrix view, Matrix projection)
    {
        _world = session;
        DrawDeathFloor(view, projection);
        DrawObstacles(view, projection);
        DrawToggles(view, projection);
        DrawMarkers(view, projection);
        DrawEntities(view, projection);
    }

    /// <summary>
    /// Blocos toggle: cubo cheio quando sólidos (igual a obstáculo, mas roxo); quando abertos,
    /// um tile fino na célula sinaliza onde o bloco vai reaparecer ao soltar a placa.
    /// </summary>
    private void DrawToggles(Matrix view, Matrix projection)
    {
        var grid = _world.Grid;
        var cubeScale = Vector3.One;
        var ghostScale = new Vector3(0.7f, 0.1f, 0.7f);

        var solid = new QueryDescription().WithAll<Toggle, GridPosition, Solid>();
        _world.World.Query(in solid, (ref GridPosition p) =>
        {
            var pos = GridView.ToWorld(grid, p.X, p.Y, p.Z, GridView.ObstacleRise);
            _cubes.Draw(pos, cubeScale, ToggleSolidColor, view, projection);
        });

        var open = new QueryDescription().WithAll<Toggle, GridPosition>().WithNone<Solid>();
        _world.World.Query(in open, (ref GridPosition p) =>
        {
            var pos = GridView.ToWorld(grid, p.X, p.Y, p.Z, GridView.MarkerRise);
            _cubes.Draw(pos, ghostScale, ToggleOpenColor, view, projection);
        });
    }

    /// <summary>
    /// Desenha marcadores que não se movem nem ocupam o grid — objetivos, portais, placas de
    /// pressão e bases atemporais — como tiles planos sobre o chão, na célula lógica (sem
    /// interpolação).
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

        // Placas de pressão: acendem quando há uma peça ocupando a célula (acionada).
        var plates = new QueryDescription().WithAll<PressurePlate, GridPosition>();
        _world.World.Query(in plates, (ref GridPosition p) =>
        {
            var pos = GridView.ToWorld(grid, p.X, p.Y, p.Z, GridView.MarkerRise);
            var color = grid.Occupant(p.X, p.Y, p.Z) is not null ? PlatePressedColor : PlateColor;
            _cubes.Draw(pos, tileScale, color, view, projection);
        });

        // Bases atemporais: tile verde fixo (congela o undo de quem pisa em cima).
        var bases = new QueryDescription().WithAll<TimelessBase, GridPosition>();
        _world.World.Query(in bases, (ref GridPosition p) =>
        {
            var pos = GridView.ToWorld(grid, p.X, p.Y, p.Z, GridView.MarkerRise);
            _cubes.Draw(pos, tileScale, TimelessBaseColor, view, projection);
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
    /// Obstáculos: o terreno sólido. Cubos cheios (1x1x1) que preenchem a célula inteira — a
    /// emenda entre blocos agora vem das arestas escurecidas pela máscara de face, não de um vão
    /// físico, então os empilhamentos ficam coesos e os topos alinham com a base das peças.
    /// </summary>
    private void DrawObstacles(Matrix view, Matrix projection)
    {
        var grid = _world.Grid;
        var scale = Vector3.One;

        var query = new QueryDescription().WithAll<Obstacle, GridPosition>();
        _world.World.Query(in query, (ref GridPosition p) =>
        {
            var pos = GridView.ToWorld(grid, p.X, p.Y, p.Z, GridView.ObstacleRise);
            _cubes.Draw(pos, scale, ObstacleColor, view, projection);
        });
    }

    private void DrawEntities(Matrix view, Matrix projection)
    {
        // Player levemente menor (0.8) pra destacar quem se controla; caixas preenchem a célula
        // inteira (1x1x1), iguais aos obstáculos.
        var playerScale = new Vector3(0.8f);
        var boxScale = Vector3.One;

        var playerColor = _world.PlayerFell ? PlayerFellColor : PlayerColor;
        var players = new QueryDescription().WithAll<Player, RenderPosition, RenderFacing>();
        _world.World.Query(in players, (ref RenderPosition r, ref RenderFacing rf) =>
        {
            _cubes.Draw(r.Value, playerScale, playerColor, view, projection);

            // O nariz é modelado no espaço local do player olhando pra Z+ e girado pelo yaw
            // VISUAL (interpolado pelo MoveAnimationSystem) — é isso que anima o giro do olhar.
            // Fica na face do olhar (offset 0.4 = a face do cubo de escala 0.8), metade pra fora.
            var rotation = Matrix.CreateRotationY(rf.Yaw) * Matrix.CreateTranslation(r.Value);
            var nose = Matrix.CreateScale(0.24f) * Matrix.CreateTranslation(0f, 0.12f, 0.4f) * rotation;
            _cubes.Draw(nose, PlayerNoseColor, view, projection);
        });

        DrawMagnetLinks(view, projection);

        // Só caixas com Solid: as quebradas perdem o tag e somem naturalmente da query.
        var boxes = new QueryDescription().WithAll<Box, RenderPosition, Solid>();
        _world.World.Query(in boxes, (ref Box b, ref RenderPosition r) =>
        {
            _cubes.Draw(r.Value, boxScale, ColorOf(b.Type), view, projection);
        });
    }

    /// <summary>
    /// Elo do grude magnético: um cubinho claro na junta entre o player e cada caixa magnética
    /// grudada (derivado da adjacência lógica, ver <see cref="Magnetism"/>), no ponto médio das
    /// posições VISUAIS — acompanha o deslize das duas peças durante as animações.
    /// </summary>
    private void DrawMagnetLinks(Matrix view, Matrix projection)
    {
        var player = _world.Spatial.First<Player>();
        if (player is null || !_world.World.Has<RenderPosition>(player.Value))
            return;
        var pr = _world.World.Get<RenderPosition>(player.Value).Value;

        var linkScale = new Vector3(0.22f);
        foreach (var (box, _, _) in Magnetism.Attached(_world))
        {
            if (!_world.World.Has<RenderPosition>(box))
                continue;
            var br = _world.World.Get<RenderPosition>(box).Value;
            _cubes.Draw((pr + br) * 0.5f, linkScale, PlayerNoseColor, view, projection);
        }
    }

    private static Color ColorOf(BoxType type) => type switch
    {
        BoxType.Light => LightBoxColor,
        BoxType.Medium => MediumBoxColor,
        BoxType.Heavy => HeavyBoxColor,
        BoxType.Fragile => FragileBoxColor,
        BoxType.Permanent => PermanentBoxColor,
        BoxType.Portal => PortalBoxColor,
        BoxType.Magnetic => MagneticBoxColor,
        _ => MediumBoxColor,
    };
}
