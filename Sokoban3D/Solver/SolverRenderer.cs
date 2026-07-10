using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Sokoban3D.Solver;

/// <summary>
/// Apresentação do modo solver (como o <c>EditorRenderer</c> é pro editor): só o HUD de texto
/// com o status do <see cref="SolverTool"/>. A cena 3D em si é o render normal do jogo — o
/// playback move as peças de verdade, então não há nada extra a desenhar.
/// </summary>
public class SolverRenderer
{
    private readonly SpriteBatch _batch;
    private readonly SpriteFont _font;

    public SolverRenderer(GraphicsDevice device, SpriteFont font)
    {
        _batch = new SpriteBatch(device);
        _font = font;
    }

    public void Draw(SolverTool tool)
    {
        _batch.Begin();
        DrawLine("SOLVER  [P] sair", new Vector2(16, 14), new Color(120, 220, 140));
        DrawLine(tool.Status, new Vector2(16, 42), Color.White);
        _batch.End();
    }

    /// <summary>Texto com sombra de 1px pra ler sobre qualquer fundo.</summary>
    private void DrawLine(string text, Vector2 pos, Color color)
    {
        if (string.IsNullOrEmpty(text))
            return;
        _batch.DrawString(_font, text, pos + Vector2.One, Color.Black);
        _batch.DrawString(_font, text, pos, color);
    }
}
