using System;
using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;
using Sokoban3D.Grid;
using Sokoban3D.Levels;

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
    private static readonly Color RailColor = new(205, 210, 220);
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

    // Caixas de clique das linhas da lista de níveis (índice = posição na lista), preenchidas a
    // cada DrawLevelListPanel — o clique acerta exatamente a linha desenhada.
    private readonly List<Rectangle> _levelHitboxes = new();

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
        (new PaletteItem(EditorBrush.Box, BoxType.Collectible), "2", "Caixa coletavel"),
        (new PaletteItem(EditorBrush.Objective), "3", "Meta"),
        (new PaletteItem(EditorBrush.Portal), "4", "Portal"),
        (new PaletteItem(EditorBrush.Player), "5", "Player"),
        (new PaletteItem(EditorBrush.Plate), "6", "Placa"),
        (new PaletteItem(EditorBrush.Toggle), "7", "Toggle"),
        (new PaletteItem(EditorBrush.TimelessBase), "8", "Base atemporal"),
        (new PaletteItem(EditorBrush.PortalBox), "9", "Caixa portal"),
        (new PaletteItem(EditorBrush.BigBox), "B", "Caixa grande"),
        (new PaletteItem(EditorBrush.Rail), "T", "Trilho"),
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
        // Lista de reordenação: painel modal sobre a cena, sem os overlays de edição.
        if (editor.ShowLevelList)
        {
            DrawLevelListPanel(editor);
            return;
        }

        if (editor.ShowPlane)
            DrawBuildPlane(session, editor, view, projection);
        DrawGhost(session, editor, view, projection);
        DrawCursor(session, editor, view, projection);
        DrawLabels(session, view, projection);
        DrawHud(editor);
    }

    /// <summary>
    /// Painel da lista de níveis (tecla L ou M): id + nome de cada nível na ordem dos ids, com a linha
    /// selecionada realçada e o nível em edição marcado. W/S navega, Shift+W/S troca o selecionado
    /// de posição (reordena os ids), Enter/clique vai pro nível. As linhas são clicáveis (hitboxes
    /// preenchidas aqui, consumidas por <see cref="HitTestLevelRow"/>).
    /// </summary>
    private void DrawLevelListPanel(LevelEditor editor)
    {
        _spriteBatch.Begin();
        float lh = _font.LineSpacing;
        var list = editor.LevelList;

        const string header = "NIVEIS";
        const string hint = "W/S: navegar   clique/Enter: ir   Shift+W/S: mover   M/L/Esc: fechar";

        int badgeSize = Math.Max(8, (int)lh - 6);
        float reserve = _font.MeasureString("   ").X + 4 * (badgeSize + 4) + _font.MeasureString("  *").X;
        float width = Math.Max(_font.MeasureString(header).X, _font.MeasureString(hint).X);
        foreach (var (id, name, _) in list)
            width = Math.Max(width, _font.MeasureString($"#{id:D2}  {name}").X + reserve);

        int rows = Math.Max(1, list.Count) + 2; // header + dica + linhas
        var panel = new Rectangle(Pad, Pad, (int)width + Pad * 2, (int)(rows * lh) + Pad * 2);
        Fill(panel, PanelBg);

        float x = panel.X + Pad, y = panel.Y + Pad;
        DrawShadowed(header, new Vector2(x, y), HeaderColor);
        y += lh;
        DrawShadowed(hint, new Vector2(x, y), HintColor);
        y += lh * 1.4f;

        _levelHitboxes.Clear();
        for (int i = 0; i < list.Count; i++)
        {
            var (id, name, badges) = list[i];
            var row = new Rectangle(panel.X, (int)y, panel.Width, (int)lh);
            bool selected = i == editor.ListSelection;
            if (selected)
                Fill(row, Color.White * 0.14f);

            Color rowColor = selected ? Color.White : TextColor;
            string baseText = id == editor.Working.Id ? $"#{id:D2}  {name}  *" : $"#{id:D2}  {name}";
            DrawShadowed(baseText, new Vector2(x, y), rowColor);

            // Quadradinhos de mecânica alinhados à direita do painel.
            DrawBadges(panel.Right - Pad, y, badges, badgeSize);

            _levelHitboxes.Add(row);
            y += lh;
        }
        if (list.Count == 0)
            DrawShadowed("(nenhum nivel salvo)", new Vector2(x, y), HintColor);

        _spriteBatch.End();
    }

    /// <summary>
    /// Desenha os quadradinhos das mecânicas presentes (swatch, igual à paleta de brushes)
    /// alinhados à direita, terminando em <paramref name="rightEdge"/>. Sombra de 1px atrás.
    /// </summary>
    private void DrawBadges(float rightEdge, float y, LevelBadges badges, int size)
    {
        const int gap = 4;
        int n = 0;
        foreach (var _ in badges.Colors())
            n++;
        if (n == 0)
            return;

        float x = rightEdge - (n * size + (n - 1) * gap);
        foreach (var color in badges.Colors())
        {
            Fill(new Rectangle((int)x + 1, (int)y + 4, size, size), Color.Black * 0.6f);
            Fill(new Rectangle((int)x, (int)y + 3, size, size), color);
            x += size + gap;
        }
    }

    /// <summary>
    /// Índice da linha da lista de níveis sob o ponto de tela, ou null se nenhuma. Usa as caixas
    /// desenhadas no último frame (mesmo padrão do <see cref="HitTestBrush"/>).
    /// </summary>
    public int? HitTestLevelRow(int x, int y)
    {
        for (int i = 0; i < _levelHitboxes.Count; i++)
            if (_levelHitboxes[i].Contains(x, y))
                return i;
        return null;
    }

    private const float GhostAlpha = 0.4f;

    /// <summary>
    /// Fantasma da peça que a brush colocaria na célula do cursor: um volume semitransparente na
    /// cor/forma aproximada da peça (cubo pros sólidos, tile plano pros marcadores, duas células
    /// pra caixa grande). Deixa claro O QUÊ e ONDE vai cair antes de confirmar — o cursor
    /// wireframe segue por cima. Ocluído por blocos à frente (DepthRead), como parte da cena.
    /// </summary>
    private void DrawGhost(GameWorld session, LevelEditor editor, Matrix view, Matrix projection)
    {
        // Borracha não coloca nada; durante um move o cursor já É a peça carregada.
        if (editor.Brush == EditorBrush.Eraser || editor.IsMoving)
            return;

        var grid = session.Grid;
        var color = ActiveColor(editor);

        _device.DepthStencilState = DepthStencilState.DepthRead;
        _device.RasterizerState = RasterizerState.CullCounterClockwise;
        _device.BlendState = BlendState.AlphaBlend;

        if (IsMarkerBrush(editor.Brush))
        {
            var pos = GridView.ToWorld(grid, editor.CursorX, editor.CursorY, editor.CursorZ, GridView.MarkerRise);
            _cubes.DrawGhost(pos, new Vector3(0.9f, 0.12f, 0.9f), color, GhostAlpha, view, projection);
            return;
        }

        // Sólidos: obstáculo é cubo cheio; as peças (caixa/player/toggle...) são um pouco menores.
        float rise = editor.Brush == EditorBrush.Obstacle ? GridView.ObstacleRise : GridView.PieceRise;
        float scale = editor.Brush == EditorBrush.Obstacle ? 1f : 0.8f;
        GhostSolid(grid, editor.CursorX, editor.CursorY, editor.CursorZ, rise, scale, color, view, projection);

        // Caixa grande ocupa duas células: prevê a segunda também, quando cabe no grid.
        if (editor.Brush == EditorBrush.BigBox)
        {
            var (ox, oz) = editor.BigBoxAxis == BigBoxAxis.X ? (1, 0) : (0, 1);
            int bx = editor.CursorX + ox, bz = editor.CursorZ + oz;
            if (bx >= 0 && bx < grid.Width && bz >= 0 && bz < grid.Depth)
                GhostSolid(grid, bx, editor.CursorY, bz, GridView.PieceRise, 0.8f, color, view, projection);
        }
    }

    private void GhostSolid(Sokoban3D.Grid.GridManager grid, int x, int y, int z, float rise, float scale,
        Color color, Matrix view, Matrix projection)
    {
        var pos = GridView.ToWorld(grid, x, y, z, rise);
        _cubes.DrawGhost(pos, new Vector3(scale), color, GhostAlpha, view, projection);
    }

    /// <summary>True pras brushes que viram marcadores planos (não ocupam o grid): meta, portal, placa, base, trilho.</summary>
    private static bool IsMarkerBrush(EditorBrush brush) => brush switch
    {
        EditorBrush.Objective or EditorBrush.Portal or EditorBrush.Plate
            or EditorBrush.TimelessBase or EditorBrush.Rail => true,
        _ => false,
    };

    /// <summary>
    /// Grade do "plano de trabalho" na camada Y ativa: uma malha translúcida cobrindo o grid na
    /// altura do cursor, com a borda em destaque e as duas linhas que cruzam o cursor pintadas na
    /// cor da brush. É o que dá referência pra construir no ar — sempre há uma superfície visível
    /// pra mirar e fica claro em que altura se está editando (convenção de editor de voxel).
    /// </summary>
    private void DrawBuildPlane(GameWorld session, LevelEditor editor, Matrix view, Matrix projection)
    {
        var grid = session.Grid;
        int w = grid.Width, d = grid.Depth;
        float y = editor.CursorY; // piso da camada: onde uma peça colocada ali repousa
        float x0 = -w / 2f, x1 = w / 2f;
        float z0 = -d / 2f, z1 = d / 2f;

        var interior = Color.White * 0.10f;
        var border = Color.White * 0.28f;
        var cross = ActiveColor(editor) * 0.7f;

        var lines = new List<VertexPositionColor>((w + d + 4) * 2);

        // Fronteiras em X (linhas correndo em Z) e em Z (linhas correndo em X); pontas em destaque.
        for (int i = 0; i <= w; i++)
        {
            float x = i - w / 2f;
            var c = (i == 0 || i == w) ? border : interior;
            lines.Add(new VertexPositionColor(new Vector3(x, y, z0), c));
            lines.Add(new VertexPositionColor(new Vector3(x, y, z1), c));
        }
        for (int j = 0; j <= d; j++)
        {
            float z = j - d / 2f;
            var c = (j == 0 || j == d) ? border : interior;
            lines.Add(new VertexPositionColor(new Vector3(x0, y, z), c));
            lines.Add(new VertexPositionColor(new Vector3(x1, y, z), c));
        }

        // Cruz do cursor: passa pelo CENTRO da célula (meia-célula fora das fronteiras da grade).
        float cx = editor.CursorX - (w - 1) / 2f;
        float cz = editor.CursorZ - (d - 1) / 2f;
        lines.Add(new VertexPositionColor(new Vector3(cx, y, z0), cross));
        lines.Add(new VertexPositionColor(new Vector3(cx, y, z1), cross));
        lines.Add(new VertexPositionColor(new Vector3(x0, y, cz), cross));
        lines.Add(new VertexPositionColor(new Vector3(x1, y, cz), cross));

        // Lê a profundidade (a grade some atrás de blocos) mas não a grava, pra não atrapalhar o
        // que vier depois. O cursor é desenhado em seguida por cima de tudo.
        _device.DepthStencilState = DepthStencilState.DepthRead;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.BlendState = BlendState.AlphaBlend;
        _cubes.DrawLines(lines.ToArray(), view, projection);
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
        DrawControlsBar(editor);
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

        // Painel de altura variável: monta as linhas e depois dimensiona pelo maior — assim
        // banner de resize, stats e status entram/saem sem recalcular tamanhos à mão.
        var lines = new List<(string, Color)[]>
        {
            new[]
            {
                ("EDITOR", HeaderColor),
                (editor.IsDirty ? " *" : "", WarnColor), // asterisco: edições não salvas
                // Renomeando: mostra o buffer com um caret em vez do nome fixo.
                editor.IsRenaming ? ($"  {editor.NameBuffer}_", OkColor) : ($"  {editor.Working.Name}", TextColor),
                ($"  #{editor.Working.Id}", HintColor), // id do nível (arquivo/portais/navegação)
                (editor.IsRenaming ? "   [Enter] ok  [Esc] cancela" : "   [Tab] jogar", HintColor),
            },
            new[]
            {
                ($"Grid {editor.Working.Width}x{editor.Working.Height}x{editor.Working.Depth}   ", TextColor),
                ($"X {editor.CursorX}  ", AxisXColor),
                ($"Y {editor.CursorY}  ", AxisYColor),
                ($"Z {editor.CursorZ}", AxisZColor),
                ("   [roda] camada Y", HintColor),
            },
            StatsSegments(editor),
        };

        if (editor.IsResizing)
            lines.Add(new[] { ("REDIMENSIONANDO  ", WarnColor), ("Shift+WASD/QE ajusta o grid", HintColor) });

        if (!string.IsNullOrEmpty(editor.Status))
            lines.Add(new[] { (editor.Status, editor.StatusIsWarning ? WarnColor : OkColor) });

        float width = 0;
        foreach (var line in lines)
            width = Math.Max(width, MeasureSegments(line));

        var panel = new Rectangle(Pad, Pad, (int)width + Pad * 2, (int)(lines.Count * lh) + Pad * 2);
        Fill(panel, PanelBg);

        float x = panel.X + Pad, y = panel.Y + Pad;
        foreach (var line in lines)
            Line(x, ref y, lh, line);

        return panel.Bottom;
    }

    /// <summary>
    /// Linha de saúde do nível: player presente (1 exato), nº de metas, nº de caixas e o badge
    /// persistente da última validação (tecla V) — feedback constante de que o design é
    /// bem-formado, sem depender do status transitório.
    /// </summary>
    private static (string, Color)[] StatsSegments(LevelEditor editor)
    {
        var lvl = editor.Working;
        int players = lvl.PlayerSpawns.Count;
        int metas = lvl.ObjectiveSpawns.Count;
        int caixas = lvl.BoxSpawns.Count + lvl.PortalBoxSpawns.Count + lvl.BigBoxSpawns.Count;
        var (badge, badgeColor) = ValidationBadge(editor.Validation);

        return new (string, Color)[]
        {
            ("Player ", TextColor), (players == 1 ? "ok" : players.ToString(), players == 1 ? OkColor : WarnColor),
            ("  Metas ", TextColor), (metas.ToString(), metas > 0 ? TextColor : WarnColor),
            ("  Caixas ", TextColor), (caixas.ToString(), TextColor),
            ("   ", TextColor), (badge, badgeColor),
        };
    }

    /// <summary>Texto+cor do badge de solvabilidade pro estado atual da validação.</summary>
    private static (string Text, Color Color) ValidationBadge(EditorValidation v) => v switch
    {
        EditorValidation.Running => ("[V] validando...", HeaderColor),
        EditorValidation.Solvable => ("resolvivel", OkColor),
        EditorValidation.Unsolvable => ("sem solucao", WarnColor),
        EditorValidation.Unknown => ("inconclusivo", WarnColor),
        EditorValidation.NoObjective => ("sem meta", WarnColor),
        _ => ("[V] validar", HintColor),
    };

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

    /// <summary>
    /// Barra de atalhos no rodapé: teclas em claro, o que fazem em apagado. Com a ajuda desligada
    /// (H), encolhe pra uma única dica — menos poluição na tela pra quem já sabe os atalhos.
    /// </summary>
    private void DrawControlsBar(LevelEditor editor)
    {
        float lh = _font.LineSpacing;

        var lines = editor.ShowHelp ? FullControls : CompactControls;

        float width = 0;
        foreach (var line in lines)
            width = Math.Max(width, MeasureSegments(line));

        var panel = new Rectangle(
            Pad, _device.Viewport.Height - (int)(lines.Length * lh) - Pad * 3,
            (int)width + Pad * 2, (int)(lines.Length * lh) + Pad * 2);
        Fill(panel, PanelBg);

        float x = panel.X + Pad, y = panel.Y + Pad;
        foreach (var line in lines)
            Line(x, ref y, lh, line);
    }

    // Rodapé de atalhos, agrupado por família (mouse / edição / arquivo+visão). Estático: não
    // depende de estado, só o ShowHelp escolhe entre a versão cheia e a compacta.
    private static readonly (string, Color)[][] FullControls =
    {
        new (string, Color)[]
        {
            ("Mouse   ", HeaderColor),
            ("Esq", TextColor), (" coloca/pinta   ", HintColor),
            ("Dir", TextColor), (" apaga   ", HintColor),
            ("Meio", TextColor), (" copia brush   ", HintColor),
            ("Ctrl+Esq", TextColor), (" move   ", HintColor),
            ("Roda", TextColor), (" camada Y   ", HintColor),
            ("Ctrl+Roda", TextColor), (" valor", HintColor),
        },
        new (string, Color)[]
        {
            ("Editar  ", HeaderColor),
            ("WASD+QE", TextColor), (" cursor   ", HintColor),
            ("Espaco", TextColor), (" coloca   ", HintColor),
            ("Del", TextColor), (" apaga   ", HintColor),
            ("Ctrl+Z/Y", TextColor), (" desfaz/refaz   ", HintColor),
            ("Shift+WASD/QE", TextColor), (" grid", HintColor),
        },
        new (string, Color)[]
        {
            ("Camera  ", HeaderColor),
            ("Alt+Arrasto", TextColor), (" orbita   ", HintColor),
            ("Alt+Roda", TextColor), (" zoom   ", HintColor),
            ("+ / -", TextColor), (" zoom", HintColor),
        },
        new (string, Color)[]
        {
            ("Arquivo ", HeaderColor),
            ("Ctrl+S", TextColor), (" salva   ", HintColor),
            ("Ctrl+O", TextColor), (" carrega   ", HintColor),
            ("Ctrl+N", TextColor), (" novo   ", HintColor),
            ("Ctrl+D", TextColor), (" duplica   ", HintColor),
            ("R", TextColor), (" renomeia   ", HintColor),
            ("L/M", TextColor), (" lista/ordena   ", HintColor),
            ("V", TextColor), (" valida   ", HintColor),
            ("G", TextColor), (" grade   ", HintColor),
            ("H", TextColor), (" ajuda", HintColor),
        },
    };

    private static readonly (string, Color)[][] CompactControls =
    {
        new (string, Color)[]
        {
            ("H", TextColor), (" ajuda   ", HintColor),
            ("Tab", TextColor), (" jogar", HintColor),
        },
    };

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
        EditorBrush.Rail => $"Trilho ({editor.RailType}: caixa em cima so sai por essas direcoes)",
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
        EditorBrush.Rail => "[T] ou [ ] cicla o tipo",
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
        EditorBrush.Rail => RailColor,
        EditorBrush.Eraser => EraserColor,
        _ => CursorColor,
    };
}
