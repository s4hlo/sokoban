using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Sokoban3D.Levels;

/// <summary>
/// Painel da lista de níveis do modo de jogo (tecla M): id + nome na ordem dos ids, com a linha
/// selecionada realçada e o nível atual marcado. Espelha o painel da lista do editor, mas sem os
/// controles de edição (renomear/reordenar). Só texto sobre a cena — o render 3D do jogo continua
/// por baixo. A apresentação do <see cref="LevelBrowser"/>, como o <c>SolverRenderer</c> é do
/// <c>SolverTool</c>.
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

    public LevelListRenderer(GraphicsDevice device, SpriteFont font)
    {
        _batch = new SpriteBatch(device);
        _font = font;
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public void Draw(LevelBrowser browser, int currentId)
    {
        var items = browser.Items;

        _batch.Begin();
        float lh = _font.LineSpacing;

        const string header = "NIVEIS";
        const string hint = "W/S: navegar   Enter: ir   M/Esc: fechar";

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

        for (int i = 0; i < items.Count; i++)
        {
            var (id, name, badges) = items[i];
            var row = new Rectangle(panel.X, (int)y, panel.Width, (int)lh);
            bool selected = i == browser.Selection;
            if (selected)
                Fill(row, Color.White * 0.14f);

            Color rowColor = selected ? Color.White : TextColor;
            string baseText = id == currentId ? $"#{id:D2}  {name}  *" : $"#{id:D2}  {name}";
            DrawShadowed(baseText, new Vector2(x, y), rowColor);

            // Quadradinhos de mecânica alinhados à direita do painel.
            DrawBadges(panel.Right - Pad, y, badges, badgeSize);
            y += lh;
        }
        if (items.Count == 0)
            DrawShadowed("(nenhum nivel salvo)", new Vector2(x, y), HintColor);

        _batch.End();
    }

    /// <summary>
    /// Desenha os quadradinhos das mecânicas presentes (swatch, igual à paleta do editor)
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
