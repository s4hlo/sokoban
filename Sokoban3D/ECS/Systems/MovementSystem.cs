using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.ECS.Systems;

/// <summary>
/// Input e movimento do player no plano X/Z. Um passo por tecla pressionada.
/// O empurrão é resolvido recursivamente célula a célula: cada caixa só anda se a
/// célula à frente puder ser liberada e ainda houver "força" (peso) sobrando. Uma
/// caixa frágil que não consegue avançar quebra em vez de bloquear.
/// </summary>
public class MovementSystem
{
    // Força do player: soma máxima de pesos de caixas que ele consegue empurrar de uma vez.
    private const int PlayerPushStrength = 2;

    // Sessão ativa do frame atual. Definida no início de cada Update; os métodos
    // auxiliares (TryClear, etc.) operam sobre ela. Permite a mesma instância de
    // sistema servir qualquer sessão da pilha de navegação.
    private GameWorld _world;
    private History _history;
    private KeyboardState _previous;

    // Peças que atravessaram um portal no turno atual, mapeadas pras posições de mundo dos portais
    // de entrada e saída — usadas pra montar a animação de teleporte. Reusado a cada jogada.
    private readonly Dictionary<Entity, (Vector3 Entry, Vector3 Exit)> _teleported = new();

    public void Update(GameWorld session, KeyboardState keyboard)
    {
        _world = session;
        _history = session.History;

        // Player caído está congelado: ignora o movimento (só Z/R/T tiram dele desse estado).
        if (session.PlayerFell)
        {
            _previous = keyboard;
            return;
        }

        (int dx, int dz) = GetFreshDirection(keyboard);
        _previous = keyboard;

        if (dx == 0 && dz == 0)
            return;

        // Resolve o player FORA de uma World.Query: o empurrão pode quebrar uma frágil
        // (Remove<Solid>), que é mudança estrutural — proibida durante a iteração de uma query.
        var player = _world.Spatial.First<Player>();
        if (player is null)
            return;

        var pos = _world.World.Get<GridPosition>(player.Value);
        TryMovePlayer(player.Value, pos, dx, dz);
    }

    private void TryMovePlayer(Entity player, GridPosition pos, int dx, int dz)
    {
        int targetX = pos.X + dx;
        int targetZ = pos.Z + dz;

        if (!_world.Grid.IsValid(targetX, pos.Y, targetZ))
            return;

        // Estado de cada peça ANTES de mutar o mundo. Se o player de fato se mover, o CommitTurn
        // grava o deslocamento líquido de cada uma (final menos isto).
        var before = Snapshot();

        // Peças que mudaram de célula via portal neste turno: o render delas precisa aparecer
        // direto no destino, sem deslizar pelo mapa. Preenchido no PushInto e no passo do player.
        _teleported.Clear();

        // Resolve onde o player de fato pára: a célula alvo pode estar livre, ser liberada por um
        // empurrão, ou (caixa portal) redirecionar pra outro lugar. null = jogada impossível, e
        // como o PushInto só muta em caminhos de sucesso, o snapshot é descartado intacto.
        var playerTo = PushInto(targetX, pos.Y, targetZ, dx, dz, PlayerPushStrength, new HashSet<Entity>());
        if (playerTo is null)
            return;

        // Parou fora da célula pra onde deu o passo → atravessou um portal: anima o teleporte.
        if (playerTo.Value.X != targetX || playerTo.Value.Y != pos.Y || playerTo.Value.Z != targetZ)
            MarkTeleport(player, targetX, pos.Y, targetZ, playerTo.Value, dx, dz);

        _world.Move(player, playerTo.Value);

        // Gravidade: toda peça que se moveu (player + caixas empurradas) cai até pousar.
        Gravity.Apply(_world, MovedSince(before));

        // Placas de pressão: a ocupação mudou, re-deriva os blocos toggle (e a queda do que
        // repousava sobre um que sumiu). Pode congelar o player (bloco aparecendo nele).
        PressurePlateSystem.Resolve(_world);

        // Quem teleportou ganha a animação de portal (entra/sai), montada agora que a célula final
        // já está assentada pela gravidade. O resto continua interpolando normalmente.
        StartTeleportAnims();

        CommitTurn(before);

        // Placa atemporal: quem terminou o turno sobre uma TimelessBase tem a pilha esvaziada.
        foreach (var e in before.Keys)
        {
            var p = _world.World.Get<GridPosition>(e);
            if (_world.Spatial.CellWith<TimelessBase>(p.X, p.Y, p.Z) is not null)
                _history.Forget(e);
        }

        // Player que repousou em y==0 está apoiado só pelo chão-morte: caiu, congela.
        var landed = _world.World.Get<GridPosition>(player);
        if (landed.Y == 0)
            _world.PlayerFell = true;
    }

    /// <summary>
    /// Posição + solidez de cada peça reversível (player, caixas, inimigos) no início do turno.
    /// A caixa verde é excluída: nunca empilha (só o R a reverte).
    /// </summary>
    private Dictionary<Entity, (GridPosition Pos, bool Solid)> Snapshot()
    {
        var snap = new Dictionary<Entity, (GridPosition, bool)>();
        var query = new QueryDescription().WithAll<GridPosition, SpawnPosition>();
        _world.World.Query(in query, (Entity e, ref GridPosition p) =>
        {
            if (_world.World.Has<Box>(e) && _world.World.Get<Box>(e).Type == BoxType.Permanent)
                return;
            snap[e] = (p, _world.World.Has<Solid>(e));
        });
        return snap;
    }

    /// <summary>
    /// Grava o deslocamento líquido + mudança de solidez de cada peça neste turno. Quem ficou parado
    /// grava inércia (delta zero), mantendo as pilhas alinhadas por turno.
    /// </summary>
    private void CommitTurn(Dictionary<Entity, (GridPosition Pos, bool Solid)> before)
    {
        foreach (var (e, snap) in before)
        {
            var p = _world.World.Get<GridPosition>(e);
            bool solid = _world.World.Has<Solid>(e);
            var change = (snap.Solid, solid) switch
            {
                (true, false) => SolidChange.Lost,
                (false, true) => SolidChange.Gained,
                _ => SolidChange.None,
            };
            _history.Record(e, new Move(p.X - snap.Pos.X, p.Y - snap.Pos.Y, p.Z - snap.Pos.Z, change));
        }
    }

    /// <summary>Peças que ocupam o grid e mudaram de posição desde o snapshot (candidatas a cair).</summary>
    private List<Entity> MovedSince(Dictionary<Entity, (GridPosition Pos, bool Solid)> before)
    {
        var movers = new List<Entity>();
        foreach (var (e, snap) in before)
        {
            if (!_world.World.Has<Solid>(e))
                continue;
            var p = _world.World.Get<GridPosition>(e);
            if (p.X != snap.Pos.X || p.Y != snap.Pos.Y || p.Z != snap.Pos.Z)
                movers.Add(e);
        }
        return movers;
    }

    /// <summary>
    /// Resolve o destino de quem quer ocupar a célula (x,y,z) entrando na direção (dx,dz) e
    /// devolve a célula onde de fato vai parar, ou null se a jogada é impossível. É a regra única
    /// do movimento, da qual empurrão e teleporte emergem sem casos especiais:
    /// <list type="bullet">
    /// <item>célula livre → é ela mesma (entra direto);</item>
    /// <item>caixa à frente → empurra-a recursivamente; quem vinha herda a célula que ela liberou;</item>
    /// <item>caixa <see cref="BoxType.Portal"/> → redireciona pra o lado oposto da parceira (a
    /// célula parceira + direção). Só se a saída estiver bloqueada o portal vira caixa empurrável.</item>
    /// </list>
    /// Como uma caixa empurrada é resolvida pelo mesmo PushInto, empurrá-la CONTRA um portal
    /// teleporta a própria caixa. Só causa mutações nos caminhos que terminam em sucesso.
    /// O conjunto <paramref name="visited"/> impede um par de portais de encadear teleporte infinito.
    /// </summary>
    private GridPosition? PushInto(int x, int y, int z, int dx, int dz, int budget, HashSet<Entity> visited)
    {
        if (!_world.Grid.IsValid(x, y, z))
            return null; // parede / fora do grid

        if (!_world.Grid.IsOccupied(x, y, z))
            return new GridPosition(x, y, z); // livre: entra direto

        Entity? occupant = _world.Grid.Occupant(x, y, z);
        if (occupant is null)
            return null;
        Entity occ = occupant.Value;

        // Portal: oferece uma saída pela parceira antes de virar caixa empurrável. visited.Add
        // devolve false se este portal já foi atravessado nesta jogada — aí pula o teleporte.
        if (_world.World.Has<PortalBox>(occ) && visited.Add(occ))
        {
            Entity? partner = FindPartner(occ);
            if (partner is not null)
            {
                var b = _world.World.Get<GridPosition>(partner.Value);
                var through = PushInto(b.X + dx, b.Y, b.Z + dz, dx, dz, budget, visited);
                if (through is not null)
                    return through; // atravessou: pára do lado oposto da parceira
            }
        }

        // Daqui pra baixo o portal é só uma caixa (sem par, ou de saída bloqueada).
        if (!_world.World.Has<Box>(occ))
            return null; // ocupado por algo que não é caixa (player, inimigo, obstáculo)

        var box = _world.World.Get<Box>(occ);
        int weight = BoxRules.Weight(box.Type);
        if (weight > budget)
            return null; // pesada demais pra força restante: não sai do lugar

        // Caixa sem peso (leve/frágil/verde/portal) não transmite força: passa orçamento 0 pra
        // frente, então não empurra nada com peso. As com peso repassam a força que sobra.
        int forwardBudget = weight == 0 ? 0 : budget - weight;
        var boxLanding = PushInto(x + dx, y, z + dz, dx, dz, forwardBudget, visited);

        if (boxLanding is not null)
        {
            // Parou fora da célula logo à frente → atravessou um portal: anima o teleporte.
            if (boxLanding.Value.X != x + dx || boxLanding.Value.Y != y || boxLanding.Value.Z != z + dz)
                MarkTeleport(occ, x + dx, y, z + dz, boxLanding.Value, dx, dz);

            // Empurra a caixa pra onde ela própria parou (uma célula à frente, ou teleportada se
            // havia um portal lá). A verde move normalmente; o CommitTurn não a grava (excluída do
            // snapshot), então o undo não a reverte — só o R.
            _world.Move(occ, boxLanding.Value);
            return new GridPosition(x, y, z); // quem vinha ocupa a célula liberada
        }

        // Não dá pra avançar. Frágil quebra (e libera a célula); o resto fica travado.
        if (box.Type == BoxType.Fragile)
        {
            BreakBox(occ);
            return new GridPosition(x, y, z);
        }

        return null;
    }

    /// <summary>
    /// O portal parceiro: o primeiro outro <see cref="PortalBox"/> de mesmo Group, ou null se não
    /// houver par. Com 3+ no mesmo grupo, vence o primeiro da iteração (arranjo não suportado).
    /// </summary>
    private Entity? FindPartner(Entity portal)
    {
        int group = _world.World.Get<PortalBox>(portal).Group;
        Entity? partner = null;
        var query = new QueryDescription().WithAll<PortalBox>();
        _world.World.Query(in query, (Entity e, ref PortalBox pb) =>
        {
            if (partner is null && e != portal && pb.Group == group)
                partner = e;
        });
        return partner;
    }

    /// <summary>
    /// Registra que <paramref name="entity"/> atravessou um portal, guardando as posições de mundo
    /// dos portais de entrada (a célula pra onde ela deu o passo) e de saída (a parceira, em
    /// <paramref name="landing"/> menos a direção). Os portais não se movem, então dá pra calcular
    /// agora, antes da gravidade reassentar a peça.
    /// </summary>
    private void MarkTeleport(Entity entity, int entryX, int entryY, int entryZ, GridPosition landing, int dx, int dz)
    {
        var entry = GridView.ToWorld(_world.Grid, entryX, entryY, entryZ, GridView.PieceRise);
        var exit = GridView.ToWorld(_world.Grid, landing.X - dx, landing.Y, landing.Z - dz, GridView.PieceRise);
        _teleported[entity] = (entry, exit);
    }

    /// <summary>
    /// Anexa a <see cref="TeleportAnim"/> em quem teleportou, partindo da posição visual atual da
    /// peça (ainda no portal de entrada, antes de a animação rodar). O <see cref="MoveAnimationSystem"/>
    /// dirige as fases e remove o componente ao terminar.
    /// </summary>
    private void StartTeleportAnims()
    {
        foreach (var (e, portals) in _teleported)
        {
            if (!_world.World.Has<RenderPosition>(e))
                continue;
            var anim = new TeleportAnim
            {
                Start = _world.World.Get<RenderPosition>(e).Value,
                Entry = portals.Entry,
                Exit = portals.Exit,
                Elapsed = 0f,
            };
            if (_world.World.Has<TeleportAnim>(e))
                _world.World.Set(e, anim);
            else
                _world.World.Add(e, anim);
        }
    }

    /// <summary>
    /// Quebra a caixa: libera a célula e remove o tag <see cref="Solid"/> — deixa de ocupar
    /// o grid e de ser desenhada. A entity persiste pra o undo conseguir reverter a quebra.
    /// </summary>
    private void BreakBox(Entity entity)
    {
        _world.Vacate(entity);
        _world.World.Remove<Solid>(entity);
    }

    /// <summary>
    /// Direção a partir das teclas WASD recém-pressionadas (uma só por frame), no plano X/Z.
    /// </summary>
    private (int dx, int dz) GetFreshDirection(KeyboardState k)
    {
        if (Pressed(k, Keys.A)) return (-1, 0);
        if (Pressed(k, Keys.D)) return (1, 0);
        if (Pressed(k, Keys.W)) return (0, -1);
        if (Pressed(k, Keys.S)) return (0, 1);
        return (0, 0);
    }

    private bool Pressed(KeyboardState current, Keys key)
        => current.IsKeyDown(key) && _previous.IsKeyUp(key);
}
