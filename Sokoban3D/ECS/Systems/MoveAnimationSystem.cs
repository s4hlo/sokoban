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

    // Suavização do giro do olhar (yaw). Um pouco mais lenta que o deslize pra o giro do
    // nariz ser visível em vez de estalar.
    private const float RotationSmoothing = 14f;
    private const float SnapAngle = 0.002f; // radianos: perto o bastante pra encaixar

    // Teleportes que terminaram neste frame: o TeleportAnim é removido após a query (mudança
    // estrutural não pode rodar durante a iteração). Campo pra não realocar a cada frame.
    private readonly List<Entity> _finishedTeleports = new();

    // Idem pros arcos do giro magnético (OrbitAnim).
    private readonly List<Entity> _finishedOrbits = new();

    public void Update(GameWorld session, float deltaSeconds)
    {
        UpdateTeleports(session, deltaSeconds);
        UpdateFacing(session, deltaSeconds);
        UpdateOrbits(session, deltaSeconds);

        float t = 1f - MathF.Exp(-Smoothing * deltaSeconds);

        // Quem está em teleporte ou em arco de giro é dirigido pelo UpdateTeleports/UpdateOrbits;
        // fica fora do deslize normal.
        var query = new QueryDescription().WithAll<GridPosition, RenderPosition>().WithNone<TeleportAnim, OrbitAnim>();
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
    /// Gira o yaw visual (<see cref="RenderFacing"/>) em direção ao ângulo do <see cref="Facing"/>
    /// lógico, sempre pelo arco mais curto (WrapAngle), com a mesma suavização exponencial do
    /// deslize. É o que anima o giro do nariz quando o olhar muda.
    /// </summary>
    private static void UpdateFacing(GameWorld session, float deltaSeconds)
    {
        float t = 1f - MathF.Exp(-RotationSmoothing * deltaSeconds);

        var query = new QueryDescription().WithAll<Facing, RenderFacing>();
        session.World.Query(in query, (ref Facing f, ref RenderFacing r) =>
        {
            float diff = MathHelper.WrapAngle(f.Yaw - r.Yaw);
            if (MathF.Abs(diff) < SnapAngle)
            {
                r.Yaw = f.Yaw;
                return;
            }
            r.Yaw = MathHelper.WrapAngle(r.Yaw + diff * t);
        });
    }

    /// <summary>
    /// Dirige o arco das caixas em giro do corpo magnético (<see cref="OrbitAnim"/>): ângulo e
    /// raio deslizam com a MESMA suavização do yaw do olhar, então a caixa varre o quarto de
    /// volta em sincronia com o giro do nariz do player. Sempre pelo arco mais curto (WrapAngle)
    /// — o quarto de volta nunca passa de π/2, então o arco curto é sempre o arco certo. Se um
    /// novo turno mudar a célula da caixa antes de o arco terminar, o destino gravado ficou
    /// velho: abandona o arco e devolve a peça ao deslize normal.
    /// </summary>
    private void UpdateOrbits(GameWorld session, float deltaSeconds)
    {
        _finishedOrbits.Clear();
        float t = 1f - MathF.Exp(-RotationSmoothing * deltaSeconds);

        var query = new QueryDescription().WithAll<OrbitAnim, GridPosition, RenderPosition>().WithNone<TeleportAnim>();
        session.World.Query(in query, (Entity e, ref OrbitAnim o, ref GridPosition grid, ref RenderPosition render) =>
        {
            var final = GridView.ToWorld(session.Grid, grid.X, grid.Y, grid.Z, GridView.PieceRise);

            float endX = o.Center.X + MathF.Cos(o.EndAngle) * o.EndRadius;
            float endZ = o.Center.Z + MathF.Sin(o.EndAngle) * o.EndRadius;
            if ((final.X - endX) * (final.X - endX) + (final.Z - endZ) * (final.Z - endZ) > SnapDistanceSq)
            {
                _finishedOrbits.Add(e);
                return;
            }

            float diff = MathHelper.WrapAngle(o.EndAngle - o.Angle);
            o.Angle += diff * t;
            o.Radius = MathHelper.Lerp(o.Radius, o.EndRadius, t);
            float ny = MathHelper.Lerp(render.Value.Y, final.Y, t);
            render.Value = new Vector3(
                o.Center.X + MathF.Cos(o.Angle) * o.Radius,
                ny,
                o.Center.Z + MathF.Sin(o.Angle) * o.Radius);

            if (MathF.Abs(MathHelper.WrapAngle(o.EndAngle - o.Angle)) < SnapAngle
                && Vector3.DistanceSquared(render.Value, final) < SnapDistanceSq)
            {
                render.Value = final;
                _finishedOrbits.Add(e);
            }
        });

        foreach (var e in _finishedOrbits)
            session.World.Remove<OrbitAnim>(e);
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

        _finishedOrbits.Clear();
        var orbits = new QueryDescription().WithAll<OrbitAnim>();
        session.World.Query(in orbits, (Entity e) => _finishedOrbits.Add(e));
        foreach (var e in _finishedOrbits)
            session.World.Remove<OrbitAnim>(e);

        var query = new QueryDescription().WithAll<GridPosition, RenderPosition>();
        session.World.Query(in query, (ref GridPosition grid, ref RenderPosition render) =>
        {
            render.Value = GridView.ToWorld(session.Grid, grid.X, grid.Y, grid.Z, GridView.PieceRise);
        });

        // O olhar também encaixa sem interpolar.
        var facings = new QueryDescription().WithAll<Facing, RenderFacing>();
        session.World.Query(in facings, (ref Facing f, ref RenderFacing r) => r.Yaw = f.Yaw);
    }
}
