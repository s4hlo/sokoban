using System.Text;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;
using Sokoban3D.Grid;

namespace Sokoban3D.Editor;

/// <summary>
/// Camada visual do editor sobre a cena do jogo: o cursor (wireframe sempre visível, colorido
/// pela brush) e o HUD de texto com o estado e as teclas. A cena em si continua sendo desenhada
/// pelo <see cref="Sokoban3D.ECS.Systems.RenderSystem"/>; aqui é só o overlay.
/// </summary>
public class EditorRenderer
{
    private static readonly Color CursorColor = new(255, 255, 255);
    private static readonly Color ObstacleColor = new(150, 160, 180);
    private static readonly Color BoxColor = new(210, 170, 90);
    private static readonly Color ObjectiveColor = new(235, 200, 70);
    private static readonly Color PortalColor = new(70, 200, 210);
    private static readonly Color PlayerColor = new(90, 150, 240);
    private static readonly Color PlateColor = new(180, 140, 230);
    private static readonly Color ToggleColor = new(130, 80, 190);
    private static readonly Color EraserColor = new(230, 90, 90);

    private readonly GraphicsDevice _device;
    private readonly CubeRenderer _cubes;
    private readonly SpriteBatch _spriteBatch;
    private readonly SpriteFont _font;

    public EditorRenderer(GraphicsDevice device, CubeRenderer cubes, SpriteFont font)
    {
        _device = device;
        _cubes = cubes;
        _font = font;
        _spriteBatch = new SpriteBatch(device);
    }

    public void Draw(GameWorld session, LevelEditor editor, Matrix view, Matrix projection)
    {
        DrawCursor(session, editor, view, projection);
        DrawLabels(session, view, projection);
        DrawHud(editor);
    }

    /// <summary>
    /// Rótulos flutuantes sobre cada peça que carrega um número não-óbvio: o grupo das placas e
    /// dos blocos toggle, e o nível-alvo dos portais. Projeta a célula no espaço de tela e
    /// escreve o texto centrado — assim dá pra ver, sem clicar, qual placa liga em qual bloco.
    /// </summary>
    private void DrawLabels(GameWorld session, Matrix view, Matrix projection)
    {
        var grid = session.Grid;
        _spriteBatch.Begin();

        var portals = new QueryDescription().WithAll<LevelPortal, GridPosition>();
        session.World.Query(in portals, (ref LevelPortal portal, ref GridPosition p) =>
            DrawTag(view, projection, grid, p, 0.5f, $"->{portal.LevelIndex}", PortalColor));

        var plates = new QueryDescription().WithAll<PressurePlate, GridPosition>();
        session.World.Query(in plates, (ref PressurePlate plate, ref GridPosition p) =>
            DrawTag(view, projection, grid, p, 0.5f, $"g{plate.Group}", PlateColor));

        var toggles = new QueryDescription().WithAll<Toggle, GridPosition>();
        session.World.Query(in toggles, (ref Toggle t, ref GridPosition p) =>
            DrawTag(view, projection, grid, p, 1.1f, $"g{t.Group}", ToggleColor));

        _spriteBatch.End();
    }

    /// <summary>Escreve <paramref name="text"/> centrado na projeção de tela da célula (com sombra).</summary>
    private void DrawTag(Matrix view, Matrix projection, GridManager grid, GridPosition p, float rise, string text, Color color)
    {
        var world = GridView.ToWorld(grid, p.X, p.Y, p.Z, rise);
        var screen = _device.Viewport.Project(world, projection, view, Matrix.Identity);
        if (screen.Z < 0f || screen.Z > 1f)
            return; // atrás da câmera

        var size = _font.MeasureString(text);
        var pos = new Vector2(screen.X - size.X / 2f, screen.Y - size.Y / 2f);
        _spriteBatch.DrawString(_font, text, pos + Vector2.One, Color.Black * 0.7f);
        _spriteBatch.DrawString(_font, text, pos, color);
    }

    private void DrawCursor(GameWorld session, LevelEditor editor, Matrix view, Matrix projection)
    {
        // Sem teste de profundidade: o cursor aparece mesmo atrás de paredes (essencial pra
        // editar células ocupadas/escondidas).
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;

        var center = GridView.ToWorld(session.Grid, editor.CursorX, editor.CursorY, editor.CursorZ, 0.5f);
        _cubes.DrawWireframe(center, new Vector3(1.02f), BrushColor(editor.Brush), view, projection);
    }

    private void DrawHud(LevelEditor editor)
    {
        var sb = new StringBuilder();
        sb.AppendLine("== EDITOR ==  (Tab: jogar)");
        sb.AppendLine($"Cursor ({editor.CursorX},{editor.CursorY},{editor.CursorZ})   " +
                      $"Grid {editor.Working.Width}x{editor.Working.Height}x{editor.Working.Depth}");
        sb.AppendLine($"Brush: {BrushLabel(editor)}");
        sb.AppendLine("Mover: WASD + Q/E    Aplicar: Espaco    Apagar: Del");
        sb.AppendLine("[1]Obstaculo [2]Caixa [3]Meta [4]Portal [5]Player [6]Placa [7]Toggle [0]Borracha");
        sb.AppendLine("Caixa: [2] cicla tipo   Portal: [ ] alvo   Placa/Toggle: [ ] grupo   Toggle: [7] inverte repouso");
        sb.AppendLine("Resize: Shift+WASD/Q/E    Salvar F5   Carregar F9   Novo N");
        if (!string.IsNullOrEmpty(editor.Status))
            sb.AppendLine(editor.Status);

        _spriteBatch.Begin();
        var text = sb.ToString();
        // Sombra simples pra o texto ficar legível sobre qualquer fundo.
        _spriteBatch.DrawString(_font, text, new Vector2(11, 11), Color.Black * 0.6f);
        _spriteBatch.DrawString(_font, text, new Vector2(10, 10), Color.White);
        _spriteBatch.End();
    }

    private static string BrushLabel(LevelEditor editor) => editor.Brush switch
    {
        EditorBrush.Box => $"Caixa ({editor.BoxType})",
        EditorBrush.Portal => $"Portal -> nivel {editor.PortalTarget}",
        EditorBrush.Plate => $"Placa (grupo {editor.Group})",
        EditorBrush.Toggle => $"Toggle (grupo {editor.Group}, {(editor.ToggleSolidByDefault ? "some ao pisar" : "aparece ao pisar")})",
        var b => b.ToString(),
    };

    private static Color BrushColor(EditorBrush brush) => brush switch
    {
        EditorBrush.Obstacle => ObstacleColor,
        EditorBrush.Box => BoxColor,
        EditorBrush.Objective => ObjectiveColor,
        EditorBrush.Portal => PortalColor,
        EditorBrush.Player => PlayerColor,
        EditorBrush.Plate => PlateColor,
        EditorBrush.Toggle => ToggleColor,
        EditorBrush.Eraser => EraserColor,
        _ => CursorColor,
    };
}
