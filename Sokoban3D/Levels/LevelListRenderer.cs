using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Sokoban3D.Levels;

/// <summary>
/// Painel da lista de níveis — a ÚNICA fonte de desenho, usada pelo modo de jogo (tecla M) e pelo
/// editor. Desenha id + nome + quadradinhos de mecânica, com a linha selecionada realçada e o
/// nível atual marcado com <c>*</c>, e registra as caixas de clique de cada linha
/// (<see cref="HitTestRow"/>) — então mouse e layout ficam idênticos nos dois modos. Só o
/// <paramref name="reorderable"/> muda a dica (o editor reordena; o jogo não). Não guarda estado
/// de lista: recebe itens/seleção de quem chama (<see cref="LevelBrowser"/> no jogo,
/// <c>LevelEditor</c> no editor).
/// </summary>
public class LevelListRenderer
{
    private static readonly Color PanelBg = new Color(10, 12, 18) * 0.82f;
    private static readonly Color HeaderColor = new(235, 200, 70);
    private static readonly Color TextColor = new(215, 215, 220);
    private static readonly Color HintColor = new(140, 145, 158);
    private const int Pad = 10;

    private readonly SpriteBatch _batch;
    private readonly SpriteFont _font;
    // Pixel branco 1x1 pra desenhar retângulos (fundo do painel, destaque da linha).
    private readonly Texture2D _pixel;

    // Caixas de clique das linhas (índice = posição na lista), preenchidas a cada Draw e
    // consumidas por HitTestRow no frame seguinte — mesmo padrão do hit-test de brush do editor.
    private readonly List<Rectangle> _hitboxes = new();

    public LevelListRenderer(GraphicsDevice device, SpriteFont font)
    {
        _batch = new SpriteBatch(device);
        _font = font;
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    /// <summary>
    /// Desenha o painel. <paramref name="currentId"/> ganha o <c>*</c>; <paramref name="reorderable"/>
    /// acrescenta a dica de reordenar (só o editor).
    /// </summary>
    public void Draw(IReadOnlyList<(int Id, string Name, LevelBadges Badges)> items,
        int selection, int currentId, bool reorderable)
    {
        _batch.Begin();
        float lh = _font.LineSpacing;

        const string header = "NIVEIS";
        string hint = reorderable
            ? "W/S: navegar   clique/Enter: ir   Shift+W/S: mover   M/Esc: fechar"
            : "W/S: navegar   clique/Enter: ir   M/Esc: fechar";

        int badgeSize = Math.Max(8, (int)lh - 6);
        float reserve = _font.MeasureString("   ").X + 4 * (badgeSize + 4) + _font.MeasureString("  *").X;
        float width = Math.Max(_font.MeasureString(header).X, _font.MeasureString(hint).X);
        foreach (var (id, name, _) in items)
            width = Math.Max(width, _font.MeasureString($"#{id:D2}  {name}").X + reserve);

        int rows = Math.Max(1, items.Count) + 2; // header + dica + linhas
        var panel = new Rectangle(Pad, Pad, (int)width + Pad * 2, (int)(rows * lh) + Pad * 2);
        Fill(panel, PanelBg);

        float x = panel.X + Pad, y = panel.Y + Pad;
        DrawShadowed(header, new Vector2(x, y), HeaderColor);
        y += lh;
        DrawShadowed(hint, new Vector2(x, y), HintColor);
        y += lh * 1.4f;

        _hitboxes.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            var (id, name, badges) = items[i];
            var row = new Rectangle(panel.X, (int)y, panel.Width, (int)lh);
            bool selected = i == selection;
            if (selected)
                Fill(row, Color.White * 0.14f);

            Color rowColor = selected ? Color.White : TextColor;
            string baseText = id == currentId ? $"#{id:D2}  {name}  *" : $"#{id:D2}  {name}";
            DrawShadowed(baseText, new Vector2(x, y), rowColor);

            // Quadradinhos de mecânica alinhados à direita do painel.
            DrawBadges(panel.Right - Pad, y, badges, badgeSize);
            _hitboxes.Add(row);
            y += lh;
        }
        if (items.Count == 0)
            DrawShadowed("(nenhum nivel salvo)", new Vector2(x, y), HintColor);

        _batch.End();
    }

    /// <summary>Índice da linha sob o ponto de tela, ou null. Usa as caixas do último Draw.</summary>
    public int? HitTestRow(int x, int y)
    {
        for (int i = 0; i < _hitboxes.Count; i++)
            if (_hitboxes[i].Contains(x, y))
                return i;
        return null;
    }

    /// <summary>
    /// Desenha os quadradinhos das mecânicas presentes (swatch, igual à paleta de brushes)
    /// alinhados à direita, terminando em <paramref name="rightEdge"/>. Sombra de 1px atrás pra
    /// destacar do fundo.
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

    private void Fill(Rectangle rect, Color color) => _batch.Draw(_pixel, rect, color);

    private void DrawShadowed(string text, Vector2 pos, Color color)
    {
        _batch.DrawString(_font, text, pos + Vector2.One, Color.Black * 0.7f);
        _batch.DrawString(_font, text, pos, color);
    }
}
