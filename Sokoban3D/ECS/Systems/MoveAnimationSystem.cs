using System;
using Arch.Core;
using Microsoft.Xna.Framework;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.ECS.Systems;

/// <summary>
/// Desliza a posição visual (RenderPosition) de cada peça em direção à sua célula no
/// grid, usando suavização exponencial (ease-out) independente de frame rate.
/// A lógica do grid continua instantânea; só o desenho é interpolado.
/// </summary>
public class MoveAnimationSystem
{
    // Quanto maior, mais rápido a peça alcança o destino. ~18 dá um deslize bem curto e seco.
    private const float Smoothing = 18f;
    private const float SnapDistanceSq = 0.0001f;

    private readonly GameWorld _world;

    public MoveAnimationSystem(GameWorld world)
    {
        _world = world;
    }

    public void Update(float deltaSeconds)
    {
        float t = 1f - MathF.Exp(-Smoothing * deltaSeconds);

        var query = new QueryDescription().WithAll<GridPosition, RenderPosition>();
        _world.World.Query(in query, (ref GridPosition grid, ref RenderPosition render) =>
        {
            var target = GridView.ToWorld(_world.Grid, grid.X, grid.Z, GridView.PieceY);
            render.Value = Vector3.Lerp(render.Value, target, t);

            // Encaixa quando já está praticamente no lugar, pra não ficar derivando pra sempre.
            if (Vector3.DistanceSquared(render.Value, target) < SnapDistanceSq)
                render.Value = target;
        });
    }
}
