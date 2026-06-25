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

    public CubeRenderer(GraphicsDevice device)
    {
        _device = device;

        _effect = new BasicEffect(device)
        {
            LightingEnabled = true,
            TextureEnabled = false,
            VertexColorEnabled = false,
        };
        _effect.EnableDefaultLighting();

        var (verts, inds) = BuildCube();
        _primitiveCount = inds.Length / 3;

        _vertices = new VertexBuffer(device, typeof(VertexPositionNormalTexture), verts.Length, BufferUsage.WriteOnly);
        _vertices.SetData(verts);

        _indices = new IndexBuffer(device, IndexElementSize.SixteenBits, inds.Length, BufferUsage.WriteOnly);
        _indices.SetData(inds);
    }

    /// <summary>
    /// Desenha um cubo na posição informada, com a escala e cor dadas.
    /// </summary>
    public void Draw(Vector3 position, Vector3 scale, Color color, Matrix view, Matrix projection)
    {
        _effect.World = Matrix.CreateScale(scale) * Matrix.CreateTranslation(position);
        _effect.View = view;
        _effect.Projection = projection;
        _effect.DiffuseColor = color.ToVector3();

        _device.SetVertexBuffer(_vertices);
        _device.Indices = _indices;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _primitiveCount);
        }
    }

    /// <summary>
    /// Monta um cubo unitário com 24 vértices (4 por face) e normais por face.
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
            inds[i + 1] = (short)(v + 1);
            inds[i + 2] = (short)(v + 2);
            inds[i + 3] = (short)(v + 0);
            inds[i + 4] = (short)(v + 2);
            inds[i + 5] = (short)(v + 3);
        }

        return (verts, inds);
    }
}
