using System;
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
/// pela brush), os rótulos 3D flutuantes e o HUD em painéis — estado do nível, paleta de
/// brushes clicável (swatch de cor, destaque da ativa, hover) com dicas contextuais da brush,
/// e a barra de atalhos no rodapé. A cena em si continua sendo desenhada pelo
/// <see cref="Sokoban3D.ECS.Systems.RenderSystem"/>; aqui é só o overlay.
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
    private static readonly Color PortalBoxColor = new(200, 70, 200);
    private static readonly Color BigBoxColor = new(210, 180, 140);
    private static readonly Color EraserColor = new(230, 90, 90);

    // Cores do HUD por papel: fundo translúcido dos painéis, cabeçalho em destaque, texto
    // comum, dicas apagadas, status verde (ok) ou laranja (aviso), e eixos no RGB clássico.
    private static readonly Color PanelBg = new Color(10, 12, 18) * 0.78f;
    private static readonly Color HeaderColor = new(235, 200, 70);
    private static readonly Color TextColor = new(215, 215, 220);
    private static readonly Color HintColor = new(140, 145, 158);
    private static readonly Color OkColor = new(120, 220, 120);
    private static readonly Color WarnColor = new(245, 165, 70);
    private static readonly Color AxisXColor = new(235, 120, 120);
    private static readonly Color AxisYColor = new(130, 225, 130);
    private static readonly Color AxisZColor = new(120, 160, 245);

    private const int Pad = 10;

    private readonly GraphicsDevice _device;
    private readonly CubeRenderer _cubes;
    private readonly SpriteBatch _spriteBatch;
    private readonly SpriteFont _font;
    // Pixel branco 1x1 pra desenhar retângulos (fundos de painel, swatches, destaques).
    private readonly Texture2D _pixel;

    // Caixas de clique das linhas da paleta, preenchidas a cada DrawPalette: assim o hit-test
    // (HitTestBrush) bate exatamente no que está desenhado, sem duplicar o layout.
    private readonly List<(PaletteItem Item, Rectangle Rect)> _brushHitboxes = new();
    // Item sob o ponteiro no último HitTestBrush (chamado todo frame), pro highlight de hover.
    private PaletteItem? _hoverItem;

    // A paleta na ordem do HUD: tecla de atalho + nome. Cada tipo de caixa tem a própria
    // linha (a tecla 2 cicla entre elas; o clique escolhe direto). A cor vem de ItemColor.
    private static readonly (PaletteItem Item, string Key, string Name)[] Palette =
    {
        (new PaletteItem(EditorBrush.Obstacle, Obstacle: ObstacleType.Normal), "1", "Obstaculo"),
        (new PaletteItem(EditorBrush.Obstacle, Obstacle: ObstacleType.Sticky), "1", "Obstaculo sticky"),
        (new PaletteItem(EditorBrush.Box, BoxType.Light), "2", "Caixa leve"),
        (new PaletteItem(EditorBrush.Box, BoxType.Medium), "2", "Caixa media"),
        (new PaletteItem(EditorBrush.Box, BoxType.Heavy), "2", "Caixa pesada"),
        (new PaletteItem(EditorBrush.Box, BoxType.Fragile), "2", "Caixa fragil"),
        (new PaletteItem(EditorBrush.Box, BoxType.Permanent), "2", "Caixa verde"),
        (new PaletteItem(EditorBrush.Box, BoxType.Magnetic), "2", "Caixa magnetica"),
        (new PaletteItem(EditorBrush.Objective), "3", "Meta"),
        (new PaletteItem(EditorBrush.Portal), "4", "Portal"),
        (new PaletteItem(EditorBrush.Player), "5", "Player"),
        (new PaletteItem(EditorBrush.Plate), "6", "Placa"),
        (new PaletteItem(EditorBrush.Toggle), "7", "Toggle"),
        (new PaletteItem(EditorBrush.TimelessBase), "8", "Base atemporal"),
        (new PaletteItem(EditorBrush.PortalBox), "9", "Caixa portal"),
        (new PaletteItem(EditorBrush.BigBox), "B", "Caixa grande"),
        (new PaletteItem(EditorBrush.Eraser), "0", "Borracha"),
    };

    public EditorRenderer(GraphicsDevice device, CubeRenderer cubes, SpriteFont font)
    {
        _device = device;
        _cubes = cubes;
        _font = font;
        _spriteBatch = new SpriteBatch(device);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData(new[] { Color.White });
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

        var portalBoxes = new QueryDescription().WithAll<PortalBox, GridPosition>();
        session.World.Query(in portalBoxes, (ref PortalBox pb, ref GridPosition p) =>
            DrawTag(view, projection, grid, p, 1.1f, $"g{pb.Group}", PortalBoxColor));

        var bigBoxes = new QueryDescription().WithAll<BigBox, GridPosition>();
        session.World.Query(in bigBoxes, (ref BigBox b, ref GridPosition p) =>
            DrawTag(view, projection, grid, p, 1.1f, b.Axis.ToString(), BigBoxColor));

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
        DrawShadowed(text, pos, color);
    }

    private void DrawCursor(GameWorld session, LevelEditor editor, Matrix view, Matrix projection)
    {
        // Sem teste de profundidade: o cursor aparece mesmo atrás de paredes (essencial pra
        // editar células ocupadas/escondidas).
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;

        // Durante um move (Ctrl+arrasto) o cursor fica branco e maior: é a "mão" carregando
        // o conteúdo até a célula de destino.
        var center = GridView.ToWorld(session.Grid, editor.CursorX, editor.CursorY, editor.CursorZ, 0.5f);
        var color = editor.IsMoving ? Color.White : ActiveColor(editor);
        var scale = new Vector3(editor.IsMoving ? 1.12f : 1.02f);
        _cubes.DrawWireframe(center, scale, color, view, projection);
    }

    // ----- HUD -----

    private void DrawHud(LevelEditor editor)
    {
        _spriteBatch.Begin();
        float bottom = DrawInfoPanel(editor);
        DrawPalettePanel(editor, bottom + Pad);
        DrawControlsBar();
        _spriteBatch.End();
    }

    /// <summary>
    /// Painel superior: título com o nome do nível, dimensões do grid, cursor com eixos
    /// coloridos (X/Y/Z no RGB clássico) e a linha de status (verde ok, laranja aviso).
    /// Retorna o Y de baixo do painel pra paleta encaixar em seguida.
    /// </summary>
    private float DrawInfoPanel(LevelEditor editor)
    {
        float lh = _font.LineSpacing;

        var title = new (string, Color)[]
        {
            ("EDITOR", HeaderColor),
            ($"  {editor.Working.Name}", TextColor),
            ("   [Tab] jogar", HintColor),
        };
        var info = new (string, Color)[]
        {
            ($"Grid {editor.Working.Width}x{editor.Working.Height}x{editor.Working.Depth}   ", TextColor),
            ($"X {editor.CursorX}  ", AxisXColor),
            ($"Y {editor.CursorY}  ", AxisYColor),
            ($"Z {editor.CursorZ}", AxisZColor),
            ("   [roda] camada Y", HintColor),
        };
        bool hasStatus = !string.IsNullOrEmpty(editor.Status);

        float width = Math.Max(MeasureSegments(title), MeasureSegments(info));
        if (hasStatus)
            width = Math.Max(width, _font.MeasureString(editor.Status).X);

        int lines = hasStatus ? 3 : 2;
        var panel = new Rectangle(Pad, Pad, (int)width + Pad * 2, (int)(lines * lh) + Pad * 2);
        Fill(panel, PanelBg);

        float x = panel.X + Pad, y = panel.Y + Pad;
        Line(x, ref y, lh, title);
        Line(x, ref y, lh, info);
        if (hasStatus)
            Line(x, ref y, lh, (editor.Status, editor.StatusIsWarning ? WarnColor : OkColor));

        return panel.Bottom;
    }

    /// <summary>
    /// Paleta vertical de brushes: cada linha tem o swatch de cor, a tecla e o nome; a ativa
    /// ganha fundo na própria cor e barra lateral, a sob o ponteiro clareia. As linhas são
    /// clicáveis (hitboxes preenchidas aqui). Abaixo, o rótulo detalhado da brush ativa e a
    /// dica contextual dela — só as teclas que importam AGORA, em vez de todas de uma vez.
    /// </summary>
    private void DrawPalettePanel(LevelEditor editor, float top)
    {
        float lh = _font.LineSpacing;
        int rowH = (int)lh + 6;
        int swatch = Math.Max(8, (int)lh - 6);

        string label = BrushLabel(editor);
        string hint = BrushHint(editor.Brush);
        int extraLines = hint.Length > 0 ? 2 : 1;

        float width = 0;
        foreach (var (_, key, name) in Palette)
            width = Math.Max(width, _font.MeasureString($"{key}  {name}").X);
        width += swatch + 8;
        width = Math.Max(width, _font.MeasureString(label).X);
        if (hint.Length > 0)
            width = Math.Max(width, _font.MeasureString(hint).X);

        var panel = new Rectangle(
            Pad, (int)top,
            (int)width + Pad * 2,
            Palette.Length * rowH + (int)(extraLines * lh) + Pad * 3);
        Fill(panel, PanelBg);

        _brushHitboxes.Clear();
        int y = panel.Y + Pad;
        foreach (var (item, key, name) in Palette)
        {
            var row = new Rectangle(panel.X, y, panel.Width, rowH);
            var color = ItemColor(item);
            // Um tipo de caixa/obstáculo só acende quando é a brush certa E o tipo ativo.
            bool active = item.Brush == editor.Brush
                && (item.Box is null || item.Box == editor.BoxType)
                && (item.Obstacle is null || item.Obstacle == editor.ObstacleType);
            if (active)
            {
                Fill(row, color * 0.28f);
                Fill(new Rectangle(row.X, row.Y, 3, row.Height), color);
            }
            else if (_hoverItem == item)
            {
                Fill(row, Color.White * 0.08f);
            }

            float tx = panel.X + Pad;
            float ty = y + (rowH - lh) / 2f;
            Fill(new Rectangle((int)tx, (int)ty + 3, swatch, swatch), color);
            DrawShadowed($"{key}  {name}", new Vector2(tx + swatch + 8, ty), active ? Color.White : TextColor);

            _brushHitboxes.Add((item, row));
            y += rowH;
        }

        float lx = panel.X + Pad;
        float ly = y + Pad / 2f;
        Line(lx, ref ly, lh, (label, ActiveColor(editor)));
        if (hint.Length > 0)
            Line(lx, ref ly, lh, (hint, HintColor));
    }

    /// <summary>Cor de um item da paleta: a do tipo de caixa/obstáculo (a mesma do jogo) ou a da brush.</summary>
    private static Color ItemColor(PaletteItem item)
        => item.Box is { } box ? Sokoban3D.ECS.Systems.RenderSystem.ColorOf(box)
        : item.Obstacle == ObstacleType.Sticky ? Sokoban3D.ECS.Systems.RenderSystem.ColorOf(ObstacleType.Sticky)
        : BrushColor(item.Brush);

    /// <summary>Cor da seleção atual do editor (brush ativa, com o tipo se for Caixa/Obstáculo).</summary>
    private static Color ActiveColor(LevelEditor editor)
        => ItemColor(new PaletteItem(editor.Brush,
            editor.Brush == EditorBrush.Box ? editor.BoxType : null,
            editor.Brush == EditorBrush.Obstacle ? editor.ObstacleType : null));

    /// <summary>Barra de atalhos no rodapé: teclas em claro, o que fazem em apagado.</summary>
    private void DrawControlsBar()
    {
        float lh = _font.LineSpacing;

        var mouseLine = new (string, Color)[]
        {
            ("Mouse   ", HeaderColor),
            ("Esq", TextColor), (" coloca/pinta   ", HintColor),
            ("Dir", TextColor), (" apaga   ", HintColor),
            ("Meio", TextColor), (" copia brush   ", HintColor),
            ("Ctrl+Esq", TextColor), (" move   ", HintColor),
            ("Roda", TextColor), (" camada Y   ", HintColor),
            ("Ctrl+Roda", TextColor), (" valor", HintColor),
        };
        var keysLine = new (string, Color)[]
        {
            ("Teclas  ", HeaderColor),
            ("WASD+QE", TextColor), (" cursor   ", HintColor),
            ("Espaco", TextColor), (" coloca   ", HintColor),
            ("Del", TextColor), (" apaga   ", HintColor),
            ("Ctrl+Z/Y", TextColor), (" desfaz/refaz   ", HintColor),
            ("Shift+WASD/QE", TextColor), (" grid   ", HintColor),
            ("Ctrl+S", TextColor), (" salva   ", HintColor),
            ("F9", TextColor), (" carrega   ", HintColor),
            ("N", TextColor), (" novo", HintColor),
        };

        float width = Math.Max(MeasureSegments(mouseLine), MeasureSegments(keysLine));
        var panel = new Rectangle(
            Pad, _device.Viewport.Height - (int)(2 * lh) - Pad * 3,
            (int)width + Pad * 2, (int)(2 * lh) + Pad * 2);
        Fill(panel, PanelBg);

        float x = panel.X + Pad, y = panel.Y + Pad;
        Line(x, ref y, lh, mouseLine);
        Line(x, ref y, lh, keysLine);
    }

    // ----- Primitivas de desenho -----

    private void Fill(Rectangle rect, Color color) => _spriteBatch.Draw(_pixel, rect, color);

    private void DrawShadowed(string text, Vector2 pos, Color color)
    {
        _spriteBatch.DrawString(_font, text, pos + Vector2.One, Color.Black * 0.7f);
        _spriteBatch.DrawString(_font, text, pos, color);
    }

    /// <summary>Desenha uma linha como segmentos coloridos lado a lado (com sombra) e avança o Y.</summary>
    private void Line(float x, ref float y, float lineHeight, params (string Text, Color Color)[] segments)
    {
        float cx = x;
        foreach (var (text, color) in segments)
        {
            DrawShadowed(text, new Vector2(cx, y), color);
            cx += _font.MeasureString(text).X;
        }
        y += lineHeight;
    }

    private float MeasureSegments((string Text, Color Color)[] segments)
    {
        float w = 0f;
        foreach (var (text, _) in segments)
            w += _font.MeasureString(text).X;
        return w;
    }

    /// <summary>
    /// Item da paleta sob o ponto de tela (<paramref name="x"/>,<paramref name="y"/>), ou null
    /// se nenhum. As caixas são as desenhadas no último frame, então o clique acerta o botão
    /// visível. Também memoriza o resultado pro highlight de hover do próximo Draw.
    /// </summary>
    public PaletteItem? HitTestBrush(int x, int y)
    {
        _hoverItem = null;
        foreach (var (item, rect) in _brushHitboxes)
        {
            if (rect.Contains(x, y))
            {
                _hoverItem = item;
                return item;
            }
        }
        return null;
    }

    private static string BrushLabel(LevelEditor editor) => editor.Brush switch
    {
        EditorBrush.Obstacle when editor.ObstacleType == ObstacleType.Sticky
            => "Obstaculo sticky (gruda quem encosta: nao da pra se afastar)",
        EditorBrush.Box => $"Caixa ({editor.BoxType})",
        EditorBrush.Portal => $"Portal -> nivel {editor.PortalTarget}",
        EditorBrush.Plate => $"Placa (grupo {editor.Group})",
        EditorBrush.Toggle => $"Toggle (grupo {editor.Group}, {(editor.ToggleSolidByDefault ? "some ao pisar" : "aparece ao pisar")}, precisa {editor.ToggleThreshold} placa(s))",
        EditorBrush.TimelessBase => "Base atemporal (apaga o historico de quem pisa)",
        EditorBrush.PortalBox => $"Caixa portal (grupo {editor.Group})",
        EditorBrush.BigBox => $"Caixa grande (eixo {editor.BigBoxAxis})",
        var b => b.ToString(),
    };

    /// <summary>Dica contextual da brush ativa: só as teclas que fazem sentido pra ela.</summary>
    private static string BrushHint(EditorBrush brush) => brush switch
    {
        EditorBrush.Obstacle => "[1] cicla o tipo",
        EditorBrush.Box => "[2] cicla o tipo",
        EditorBrush.Portal => "[ ] ou Ctrl+Roda: nivel alvo",
        EditorBrush.Plate => "[ ] ou Ctrl+Roda: grupo",
        EditorBrush.Toggle => "[7] inverte repouso   [ ] grupo   , . placas p/ acionar",
        EditorBrush.PortalBox => "[ ] ou Ctrl+Roda: grupo",
        EditorBrush.BigBox => "[B] inverte o eixo",
        _ => "",
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
        EditorBrush.PortalBox => PortalBoxColor,
        EditorBrush.BigBox => BigBoxColor,
        EditorBrush.Eraser => EraserColor,
        _ => CursorColor,
    };
}
