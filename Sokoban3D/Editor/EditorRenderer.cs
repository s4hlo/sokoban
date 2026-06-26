using System.Collections.Generic;
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
    private static readonly Color TimelessBaseColor = new(60, 200, 120);
    private static readonly Color EraserColor = new(230, 90, 90);

    private readonly GraphicsDevice _device;
    private readonly CubeRenderer _cubes;
    private readonly SpriteBatch _spriteBatch;
    private readonly SpriteFont _font;

    // Caixas de clique dos botões de brush no HUD, preenchidas a cada DrawBrushKeys: assim o
    // hit-test (HitTestBrush) bate exatamente no que está desenhado, sem duplicar o layout.
    private readonly List<(EditorBrush Brush, Rectangle Rect)> _brushHitboxes = new();

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
            DrawTag(view, projection, grid, p, 1.1f, $"g{t.Group} x{(t.Threshold <= 0 ? 1 : t.Threshold)}", ToggleColor));

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

    // Cores do HUD por papel: cabeçalho em destaque, texto comum, dicas apagadas, status em verde.
    private static readonly Color HeaderColor = new(235, 200, 70);
    private static readonly Color TextColor = new(205, 205, 210);
    private static readonly Color HintColor = new(140, 140, 150);
    private static readonly Color StatusColor = new(120, 220, 120);

    // Teclas de brush, cada uma colorida pela própria cor (a ativa fica acesa, as demais apagadas).
    private static readonly (EditorBrush Brush, string Label)[] BrushKeys =
    {
        (EditorBrush.Obstacle, "[1]Obstaculo "),
        (EditorBrush.Box, "[2]Caixa "),
        (EditorBrush.Objective, "[3]Meta "),
        (EditorBrush.Portal, "[4]Portal "),
        (EditorBrush.Player, "[5]Player "),
        (EditorBrush.Plate, "[6]Placa "),
        (EditorBrush.Toggle, "[7]Toggle "),
        (EditorBrush.TimelessBase, "[8]Base "),
        (EditorBrush.Eraser, "[0]Borracha"),
    };

    private void DrawHud(LevelEditor editor)
    {
        float x = 10f, y = 10f;
        float lh = _font.LineSpacing;

        _spriteBatch.Begin();

        Line(x, ref y, lh, ("== EDITOR ==  ", HeaderColor), ("(Tab: jogar)", HintColor));
        Line(x, ref y, lh, ($"Cursor ({editor.CursorX},{editor.CursorY},{editor.CursorZ})   " +
                            $"Grid {editor.Working.Width}x{editor.Working.Height}x{editor.Working.Depth}", TextColor));
        Line(x, ref y, lh, ("Brush: ", TextColor), (BrushLabel(editor), BrushColor(editor.Brush)));
        Line(x, ref y, lh, ("Mover: WASD + Q/E    Aplicar: Espaco    Apagar: Del", HintColor));
        Line(x, ref y, lh, ("Mouse: clique posiciona / repete aplica   Segurar+arrastar pinta   Botao dir: apaga   (camada Y: Q/E)", HintColor));
        DrawBrushKeys(x, ref y, lh, editor.Brush);
        Line(x, ref y, lh, ("Caixa: [2] cicla tipo   [ ] grupo/alvo sob o cursor (ou do proximo)   Toggle: [7] inverte repouso   , . placas p/ acionar", HintColor));
        Line(x, ref y, lh, ("Resize: Shift+WASD/Q/E    Salvar F5   Carregar F9   Novo N", HintColor));
        if (!string.IsNullOrEmpty(editor.Status))
            Line(x, ref y, lh, (editor.Status, StatusColor));

        _spriteBatch.End();
    }

    /// <summary>Desenha uma linha como segmentos coloridos lado a lado (com sombra) e avança o Y.</summary>
    private void Line(float x, ref float y, float lineHeight, params (string Text, Color Color)[] segments)
    {
        float cx = x;
        foreach (var (text, color) in segments)
        {
            _spriteBatch.DrawString(_font, text, new Vector2(cx + 1, y + 1), Color.Black * 0.7f);
            _spriteBatch.DrawString(_font, text, new Vector2(cx, y), color);
            cx += _font.MeasureString(text).X;
        }
        y += lineHeight;
    }

    /// <summary>Linha das teclas de brush: cada token na sua cor; a brush ativa acesa, as demais apagadas.</summary>
    private void DrawBrushKeys(float x, ref float y, float lineHeight, EditorBrush active)
    {
        _brushHitboxes.Clear();
        float cx = x;
        foreach (var (brush, label) in BrushKeys)
        {
            float w = _font.MeasureString(label).X;
            var color = brush == active ? Color.White : BrushColor(brush) * 0.5f;
            _spriteBatch.DrawString(_font, label, new Vector2(cx + 1, y + 1), Color.Black * 0.7f);
            _spriteBatch.DrawString(_font, label, new Vector2(cx, y), color);
            _brushHitboxes.Add((brush, new Rectangle((int)cx, (int)y, (int)w, (int)lineHeight)));
            cx += w;
        }
        y += lineHeight;
    }

    /// <summary>
    /// Brush cujo rótulo no HUD está sob o ponto de tela (<paramref name="x"/>,<paramref name="y"/>),
    /// ou null se nenhum. As caixas são as desenhadas no último frame, então o clique acerta o
    /// botão visível.
    /// </summary>
    public EditorBrush? HitTestBrush(int x, int y)
    {
        foreach (var (brush, rect) in _brushHitboxes)
            if (rect.Contains(x, y))
                return brush;
        return null;
    }

    private static string BrushLabel(LevelEditor editor) => editor.Brush switch
    {
        EditorBrush.Box => $"Caixa ({editor.BoxType})",
        EditorBrush.Portal => $"Portal -> nivel {editor.PortalTarget}",
        EditorBrush.Plate => $"Placa (grupo {editor.Group})",
        EditorBrush.Toggle => $"Toggle (grupo {editor.Group}, {(editor.ToggleSolidByDefault ? "some ao pisar" : "aparece ao pisar")}, precisa {editor.ToggleThreshold} placa(s))",
        EditorBrush.TimelessBase => "Base atemporal (congela o undo de quem pisa)",
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
        EditorBrush.TimelessBase => TimelessBaseColor,
        EditorBrush.Eraser => EraserColor,
        _ => CursorColor,
    };
}
