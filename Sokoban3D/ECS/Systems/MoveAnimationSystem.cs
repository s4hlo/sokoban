using System;
using System.Collections.Generic;
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

    // Duração total da animação de teleporte (entra no portal + sai do parceiro), em segundos.
    private const float TeleportDuration = 0.24f;

    // Teleportes que terminaram neste frame: o TeleportAnim é removido após a query (mudança
    // estrutural não pode rodar durante a iteração). Campo pra não realocar a cada frame.
    private readonly List<Entity> _finishedTeleports = new();

    public void Update(GameWorld session, float deltaSeconds)
    {
        UpdateTeleports(session, deltaSeconds);

        float t = 1f - MathF.Exp(-Smoothing * deltaSeconds);

        // Quem está em teleporte é dirigido pelo UpdateTeleports; fica fora do deslize normal.
        var query = new QueryDescription().WithAll<GridPosition, RenderPosition>().WithNone<TeleportAnim>();
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
    /// Dirige a animação de teleporte das peças com <see cref="TeleportAnim"/>, em duas metades:
    /// na primeira desliza de <c>Start</c> pra dentro do portal de entrada (ease-in, acelerando pra
    /// "ser sugada"); na segunda brota do portal de saída e desliza até a célula final (ease-out,
    /// freando no destino). Ao completar, remove o componente e a peça volta ao deslize normal.
    /// </summary>
    private void UpdateTeleports(GameWorld session, float deltaSeconds)
    {
        _finishedTeleports.Clear();

        var query = new QueryDescription().WithAll<TeleportAnim, GridPosition, RenderPosition>();
        session.World.Query(in query, (Entity e, ref TeleportAnim anim, ref GridPosition grid, ref RenderPosition render) =>
        {
            anim.Elapsed += deltaSeconds;
            float u = anim.Elapsed / TeleportDuration;
            var final = GridView.ToWorld(session.Grid, grid.X, grid.Y, grid.Z, GridView.PieceRise);

            if (u >= 1f)
            {
                render.Value = final;
                _finishedTeleports.Add(e);
                return;
            }

            if (u < 0.5f)
            {
                float p = u / 0.5f;          // 0..1 na entrada
                p *= p;                       // ease-in: acelera pra dentro do portal
                render.Value = Vector3.Lerp(anim.Start, anim.Entry, p);
            }
            else
            {
                float p = (u - 0.5f) / 0.5f;  // 0..1 na saída
                p = 1f - (1f - p) * (1f - p); // ease-out: freia chegando na célula final
                render.Value = Vector3.Lerp(anim.Exit, final, p);
            }
        });

        foreach (var e in _finishedTeleports)
            session.World.Remove<TeleportAnim>(e);
    }

    /// <summary>
    /// Encaixa toda peça na sua célula do grid imediatamente, sem interpolar. Usado pelo reset
    /// total (F), que deve ser instantâneo: a peça reaparece onde estava, sem deslizar. Cancela
    /// também qualquer teleporte em andamento (senão ele continuaria dirigindo o render).
    /// </summary>
    public void SnapAll(GameWorld session)
    {
        _finishedTeleports.Clear();
        var anims = new QueryDescription().WithAll<TeleportAnim>();
        session.World.Query(in anims, (Entity e) => _finishedTeleports.Add(e));
        foreach (var e in _finishedTeleports)
            session.World.Remove<TeleportAnim>(e);

        var query = new QueryDescription().WithAll<GridPosition, RenderPosition>();
        session.World.Query(in query, (ref GridPosition grid, ref RenderPosition render) =>
        {
            render.Value = GridView.ToWorld(session.Grid, grid.X, grid.Y, grid.Z, GridView.PieceRise);
        });
    }
}
