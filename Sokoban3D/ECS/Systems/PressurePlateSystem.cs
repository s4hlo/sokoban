using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.ECS.Systems;

/// <summary>
/// Resolve as placas de pressão: o estado de cada bloco <see cref="Toggle"/> é DERIVADO da
/// ocupação das placas do seu grupo, nunca armazenado à parte. Uma placa está pressionada
/// quando há uma peça que ocupa o grid (player/caixa) em cima dela; um grupo está acionado se
/// QUALQUER placa dele estiver pressionada (lógica OR). Acionado, o bloco inverte seu
/// <see cref="Toggle.SolidByDefault"/>.
///
/// É autoridade única sobre o tag <see cref="Solid"/> dos toggles: ligar/desligar passa por
/// aqui, gravando o estado anterior no <c>record</c> da ação — assim o undo o reverte pela
/// mesma máquina das caixas frágeis. Roda como último passo de cada movimento (a ocupação só
/// muda em movimentos), no load e no restart.
/// </summary>
public static class PressurePlateSystem
{
    /// <summary>
    /// Re-deriva todos os toggles a partir das placas pressionadas e aplica a diferença,
    /// gravando o estado anterior dos blocos afetados (e a queda do que repousava sobre um
    /// que sumiu) em <paramref name="record"/>. Um bloco que deveria aparecer sobre uma célula
    /// ocupada fica PENDENTE (não materializa) — e se quem ocupa é o player, ele congela
    /// (<see cref="GameWorld.PlayerFell"/>): preso, igual à queda, só sai com Z.
    /// </summary>
    public static void Resolve(GameWorld session, List<EntityState> record)
    {
        var pressed = PressedGroups(session);

        // Coleta antes de mutar: Add/Remove de Solid é mudança estrutural, proibida durante a
        // iteração de uma World.Query.
        var toOpen = new List<(Entity Entity, GridPosition Pos)>();
        var toSolidify = new List<(Entity Entity, GridPosition Pos)>();

        var query = new QueryDescription().WithAll<Toggle, GridPosition>();
        session.World.Query(in query, (Entity e, ref Toggle t, ref GridPosition p) =>
        {
            bool desiredSolid = t.SolidByDefault ^ pressed.Contains(t.Group);
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
            record.Add(new EntityState(e, p, true));
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

            record.Add(new EntityState(e, p, false));
            session.World.Add(e, new Solid());
            session.Grid.Place(p.X, p.Y, p.Z, e);
        }

        if (fallers.Count > 0)
            Gravity.Apply(session, fallers, record);
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

        // Placas pressionadas já no spawn ainda devem inverter seus blocos. Sem histórico.
        Resolve(session, new List<EntityState>());
    }

    /// <summary>Grupos com pelo menos uma placa pressionada (peça ocupando sua célula).</summary>
    private static HashSet<int> PressedGroups(GameWorld session)
    {
        var pressed = new HashSet<int>();
        var query = new QueryDescription().WithAll<PressurePlate, GridPosition>();
        session.World.Query(in query, (ref PressurePlate plate, ref GridPosition p) =>
        {
            if (session.Grid.Occupant(p.X, p.Y, p.Z) is not null)
                pressed.Add(plate.Group);
        });
        return pressed;
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
