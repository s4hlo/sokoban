using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.ECS.Systems;

/// <summary>
/// Resolve as placas de pressão: o estado de cada bloco <see cref="Toggle"/> é DERIVADO da
/// ocupação das placas do seu grupo, nunca armazenado à parte. Uma placa está pressionada
/// quando há uma peça que ocupa o grid (player/caixa) em cima dela. Cada bloco aciona quando o
/// número de placas pisadas do seu grupo atinge o seu <see cref="Toggle.Threshold"/> (1 = qualquer
/// placa, lógica OR; total de placas do grupo = só com TODAS, lógica AND). Acionado, o bloco
/// inverte seu <see cref="Toggle.SolidByDefault"/>.
///
/// É autoridade única sobre o tag <see cref="Solid"/> dos toggles. A solidez do toggle é estado
/// puramente DERIVADO das placas — não tem pilha, não entra no histórico, o undo é totalmente
/// agnóstico a ela. Esta Resolve é a ÚNICA dona desse estado: roda no fim de cada movimento, no
/// load, no restart e depois de um undo (o undo só restaura as posições das peças; o
/// <see cref="Game1"/> chama esta Resolve em seguida pra re-derivar os toggles a partir do estado
/// restaurado). A queda das peças que repousavam sobre um bloco que sumiu é movimento real e é
/// capturada pelo histórico via snapshot do turno, não por aqui.
/// </summary>
public static class PressurePlateSystem
{
    /// <summary>
    /// Re-deriva todos os toggles a partir das placas pressionadas e aplica a diferença. Um bloco
    /// que deveria aparecer sobre uma célula ocupada fica PENDENTE (não materializa) — e se quem
    /// ocupa é o player, ele congela (<see cref="GameWorld.PlayerFell"/>): preso, igual à queda, só
    /// sai com Z.
    /// </summary>
    public static void Resolve(GameWorld session)
    {
        var pressedCount = PressedCounts(session);

        // Coleta antes de mutar: Add/Remove de Solid é mudança estrutural, proibida durante a
        // iteração de uma World.Query.
        var toOpen = new List<(Entity Entity, GridPosition Pos)>();
        var toSolidify = new List<(Entity Entity, GridPosition Pos)>();

        var query = new QueryDescription().WithAll<Toggle, GridPosition>();
        session.World.Query(in query, (Entity e, ref Toggle t, ref GridPosition p) =>
        {
            // Aciona quando o nº de placas pisadas do grupo atinge o threshold (0 = 1 = OR).
            int threshold = t.Threshold <= 0 ? 1 : t.Threshold;
            bool triggered = pressedCount.GetValueOrDefault(t.Group) >= threshold;
            bool desiredSolid = t.SolidByDefault ^ triggered;
            bool isSolid = session.World.Has<Solid>(e);
            if (desiredSolid == isSolid)
                return;

            if (desiredSolid)
                toSolidify.Add((e, p));
            else
                toOpen.Add((e, p));
        });

        // Abre os blocos (some): libera o grid e tira o Solid. Peças que repousavam em cima
        // viram candidatas a cair.
        var fallers = new List<Entity>();
        foreach (var (e, p) in toOpen)
        {
            CollectStackAbove(session, p, fallers);
            session.Grid.Vacate(p.X, p.Y, p.Z);
            session.World.Remove<Solid>(e);
        }

        // Materializa os blocos (aparece) — só onde a célula está livre. Ocupada, fica pendente
        // (uma próxima Resolve tenta de novo); se quem ocupa é o player, congela-o.
        foreach (var (e, p) in toSolidify)
        {
            var occupant = session.Grid.Occupant(p.X, p.Y, p.Z);
            if (occupant is not null)
            {
                if (session.World.Has<Player>(occupant.Value))
                    session.PlayerFell = true;
                continue; // pendente: não materializa sobre algo
            }

            session.World.Add(e, new Solid());
            session.Grid.Place(p.X, p.Y, p.Z, e);
        }

        if (fallers.Count > 0)
            Gravity.Apply(session, fallers);
    }

    /// <summary>
    /// Reseta todos os toggles ao estado de repouso (<see cref="Toggle.SolidByDefault"/>) e
    /// re-deriva pelas placas. Usado pelo restart, depois que as peças voltaram aos spawns:
    /// reconstrói a ocupação dos blocos sólidos (o restart zera o grid) sem registrar histórico
    /// — o snapshot do restart já guardou o estado pré-R dos toggles.
    /// </summary>
    public static void Reset(GameWorld session)
    {
        var toAdd = new List<(Entity Entity, GridPosition Pos)>();
        var toRemove = new List<Entity>();

        var query = new QueryDescription().WithAll<Toggle, GridPosition>();
        session.World.Query(in query, (Entity e, ref Toggle t, ref GridPosition p) =>
        {
            bool isSolid = session.World.Has<Solid>(e);
            if (t.SolidByDefault && !isSolid)
                toAdd.Add((e, p));
            else if (!t.SolidByDefault && isSolid)
                toRemove.Add(e);
            else if (t.SolidByDefault)
                // Já sólido, mas o grid foi zerado pelo restart: reocupa a célula.
                session.Grid.Place(p.X, p.Y, p.Z, e);
        });

        foreach (var e in toRemove)
            session.World.Remove<Solid>(e);
        foreach (var (e, p) in toAdd)
        {
            session.World.Add(e, new Solid());
            session.Grid.Place(p.X, p.Y, p.Z, e);
        }

        // Placas pressionadas já no spawn ainda devem inverter seus blocos.
        Resolve(session);
    }

    /// <summary>Quantas placas de cada grupo estão pressionadas (peça ocupando sua célula).</summary>
    private static Dictionary<int, int> PressedCounts(GameWorld session)
    {
        var counts = new Dictionary<int, int>();
        var query = new QueryDescription().WithAll<PressurePlate, GridPosition>();
        session.World.Query(in query, (ref PressurePlate plate, ref GridPosition p) =>
        {
            if (session.Grid.Occupant(p.X, p.Y, p.Z) is not null)
                counts[plate.Group] = counts.GetValueOrDefault(plate.Group) + 1;
        });
        return counts;
    }

    /// <summary>Pilha contígua de peças que repousa diretamente sobre a célula do bloco aberto.</summary>
    private static void CollectStackAbove(GameWorld session, GridPosition below, List<Entity> into)
    {
        int ny = below.Y + 1;
        while (session.Grid.Occupant(below.X, ny, below.Z) is { } e)
        {
            if (!into.Contains(e))
                into.Add(e);
            ny++;
        }
    }
}
