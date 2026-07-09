using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Sokoban3D.Core;

/// <summary>
/// Desenha cubos coloridos no espaço 3D usando um BasicEffect com iluminação simples.
/// O cubo base é unitário (1x1x1), centrado na origem; a posição/escala vem por parâmetro.
/// </summary>
public class CubeRenderer
{
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly VertexBuffer _vertices;
    private readonly IndexBuffer _indices;
    private readonly int _primitiveCount;

    // Cubo de arestas (wireframe) pro cursor do editor: 8 cantos, 12 linhas. Efeito sem
    // iluminação pra a cor sair chapada (mais legível que um cubo sombreado).
    private readonly BasicEffect _wireEffect;
    private readonly VertexBuffer _wireVertices;
    private readonly IndexBuffer _wireIndices;

    // Máscara de face: textura branca que só escurece as bordas. Modula o DiffuseColor — cada
    // cubo mantém a própria cor, mas com as arestas sombreadas pra ler melhor o volume.
    public CubeRenderer(GraphicsDevice device, Texture2D faceMask)
    {
        _device = device;

        _effect = new BasicEffect(device)
        {
            LightingEnabled = true,
            TextureEnabled = true,
            Texture = faceMask,
            VertexColorEnabled = false,
        };
        _effect.EnableDefaultLighting();

        var (verts, inds) = BuildCube();
        _primitiveCount = inds.Length / 3;

        _vertices = new VertexBuffer(device, typeof(VertexPositionNormalTexture), verts.Length, BufferUsage.WriteOnly);
        _vertices.SetData(verts);

        _indices = new IndexBuffer(device, IndexElementSize.SixteenBits, inds.Length, BufferUsage.WriteOnly);
        _indices.SetData(inds);

        _wireEffect = new BasicEffect(device)
        {
            LightingEnabled = false,
            TextureEnabled = false,
            VertexColorEnabled = false,
        };

        var (wverts, winds) = BuildWireCube();
        _wireVertices = new VertexBuffer(device, typeof(VertexPosition), wverts.Length, BufferUsage.WriteOnly);
        _wireVertices.SetData(wverts);
        _wireIndices = new IndexBuffer(device, IndexElementSize.SixteenBits, winds.Length, BufferUsage.WriteOnly);
        _wireIndices.SetData(winds);
    }

    /// <summary>
    /// Desenha um cubo na posição informada, com a escala e cor dadas.
    /// </summary>
    public void Draw(Vector3 position, Vector3 scale, Color color, Matrix view, Matrix projection)
        => Draw(Matrix.CreateScale(scale) * Matrix.CreateTranslation(position), color, view, projection);

    /// <summary>
    /// Desenha um cubo com a matriz de mundo completa — pra quem precisa de rotação (ex.: o
    /// nariz do player girando com o yaw visual), além de escala/posição.
    /// </summary>
    public void Draw(Matrix world, Color color, Matrix view, Matrix projection)
    {
        _effect.World = world;
        _effect.View = view;
        _effect.Projection = projection;
        _effect.DiffuseColor = color.ToVector3();

        // A máscara cobre exatamente a face (UV 0..1); clamp evita a borda sangrar pro lado oposto.
        _device.SamplerStates[0] = SamplerState.LinearClamp;
        _device.SetVertexBuffer(_vertices);
        _device.Indices = _indices;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _primitiveCount);
        }
    }

    /// <summary>
    /// Desenha apenas as arestas de um cubo (wireframe) na posição/escala dadas. Usado como
    /// cursor do editor. O chamador controla o estado de profundidade (o editor o desenha por
    /// cima da geometria pra ficar sempre visível).
    /// </summary>
    public void DrawWireframe(Vector3 position, Vector3 scale, Color color, Matrix view, Matrix projection)
    {
        _wireEffect.World = Matrix.CreateScale(scale) * Matrix.CreateTranslation(position);
        _wireEffect.View = view;
        _wireEffect.Projection = projection;
        _wireEffect.DiffuseColor = color.ToVector3();

        _device.SetVertexBuffer(_wireVertices);
        _device.Indices = _wireIndices;

        foreach (var pass in _wireEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawIndexedPrimitives(PrimitiveType.LineList, 0, 0, 12);
        }
    }

    /// <summary>Os 8 cantos de um cubo unitário e as 12 arestas (índices de linha).</summary>
    private static (VertexPosition[] verts, short[] inds) BuildWireCube()
    {
        var verts = new VertexPosition[8];
        int i = 0;
        for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
                for (int x = 0; x <= 1; x++)
                    verts[i++] = new VertexPosition(new Vector3(x - 0.5f, y - 0.5f, z - 0.5f));

        // Índice de canto: bit0=x, bit1=z, bit2=y. Arestas = pares que diferem em 1 bit.
        short[] inds =
        {
            0, 1, 1, 3, 3, 2, 2, 0, // base (y=0)
            4, 5, 5, 7, 7, 6, 6, 4, // topo (y=1)
            0, 4, 1, 5, 2, 6, 3, 7, // verticais
        };

        return (verts, inds);
    }

    /// <summary>
    /// Monta um cubo unitário com 24 vértices (4 por face) e normais por face.
    /// Os triângulos são enrolados em sentido horário visto de fora (a,c,b / a,d,c), que é
    /// a convenção de face-da-frente do MonoGame (RasterizerState.CullCounterClockwise).
    /// Assim o descarte de face traseira mantém as faces externas e some com as internas.
    /// </summary>
    private static (VertexPositionNormalTexture[] verts, short[] inds) BuildCube()
    {
        Vector3[] faceNormals =
        {
            new(0, 0, 1),   // frente
            new(0, 0, -1),  // trás
            new(1, 0, 0),   // direita
            new(-1, 0, 0),  // esquerda
            new(0, 1, 0),   // topo
            new(0, -1, 0),  // base
        };

        var verts = new VertexPositionNormalTexture[24];
        var inds = new short[36];

        for (int f = 0; f < 6; f++)
        {
            Vector3 n = faceNormals[f];
            // Dois vetores tangentes à face, derivados da normal.
            Vector3 up = (n.Y == 0) ? Vector3.Up : Vector3.Forward;
            Vector3 right = Vector3.Cross(up, n);
            up = Vector3.Cross(n, right);

            Vector3 center = n * 0.5f;
            Vector3 a = center - right * 0.5f - up * 0.5f;
            Vector3 b = center + right * 0.5f - up * 0.5f;
            Vector3 c = center + right * 0.5f + up * 0.5f;
            Vector3 d = center - right * 0.5f + up * 0.5f;

            int v = f * 4;
            verts[v + 0] = new VertexPositionNormalTexture(a, n, new Vector2(0, 1));
            verts[v + 1] = new VertexPositionNormalTexture(b, n, new Vector2(1, 1));
            verts[v + 2] = new VertexPositionNormalTexture(c, n, new Vector2(1, 0));
            verts[v + 3] = new VertexPositionNormalTexture(d, n, new Vector2(0, 0));

            int i = f * 6;
            inds[i + 0] = (short)(v + 0);
            inds[i + 1] = (short)(v + 2);
            inds[i + 2] = (short)(v + 1);
            inds[i + 3] = (short)(v + 0);
            inds[i + 4] = (short)(v + 3);
            inds[i + 5] = (short)(v + 2);
        }

        return (verts, inds);
    }
}
