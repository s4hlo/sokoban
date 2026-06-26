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

    // Quão perto (no plano X/Z) a peça precisa estar do destino antes de começar a descer.
    // Mantém a queda travada até a marcha horizontal terminar, formando o "L".
    private const float HorizontalArrivedSq = 0.0025f;

    public void Update(GameWorld session, float deltaSeconds)
    {
        float t = 1f - MathF.Exp(-Smoothing * deltaSeconds);

        var query = new QueryDescription().WithAll<GridPosition, RenderPosition>();
        session.World.Query(in query, (ref GridPosition grid, ref RenderPosition render) =>
        {
            var target = GridView.ToWorld(session.Grid, grid.X, grid.Y, grid.Z, GridView.PieceRise);
            var current = render.Value;

            // Desliza primeiro no plano X/Z. O Y (queda) só se move depois que a peça já
            // chegou na coluna de destino — assim "anda pra frente e DEPOIS cai" em vez de
            // cortar na diagonal. Em movimento plano, target.Y == current.Y, então o estágio
            // vertical não muda nada.
            float nx = MathHelper.Lerp(current.X, target.X, t);
            float nz = MathHelper.Lerp(current.Z, target.Z, t);

            float horizontalGapSq = (target.X - nx) * (target.X - nx) + (target.Z - nz) * (target.Z - nz);
            float ny = horizontalGapSq < HorizontalArrivedSq
                ? MathHelper.Lerp(current.Y, target.Y, t)
                : current.Y;

            render.Value = new Vector3(nx, ny, nz);

            // Encaixa quando já está praticamente no lugar, pra não ficar derivando pra sempre.
            if (Vector3.DistanceSquared(render.Value, target) < SnapDistanceSq)
                render.Value = target;
        });
    }

    /// <summary>
    /// Encaixa toda peça na sua célula do grid imediatamente, sem interpolar. Usado pelo
    /// undo (Z), que deve ser instantâneo: a peça reaparece onde estava, sem deslizar.
    /// </summary>
    public void SnapAll(GameWorld session)
    {
        var query = new QueryDescription().WithAll<GridPosition, RenderPosition>();
        session.World.Query(in query, (ref GridPosition grid, ref RenderPosition render) =>
        {
            render.Value = GridView.ToWorld(session.Grid, grid.X, grid.Y, grid.Z, GridView.PieceRise);
        });
    }
}
