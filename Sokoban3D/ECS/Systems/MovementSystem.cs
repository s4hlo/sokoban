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
///
/// SEM caixa magnética grudada (ver <see cref="Magnetism"/>), o movimento é livre: cada comando
/// anda direto na direção apertada e o olhar só acompanha. COM alguma grudada, o player vira um
/// corpo rígido (ele + as caixas, estilo Stephen's Sausage Roll): comando alinhado com o olhar
/// (de frente ou de costas) translada o corpo inteiro — as caixas grudadas empurram o que
/// encontram —; comando perpendicular GIRA o corpo, com as caixas varrendo o arco.
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

    // True se algum PushInto da jogada atual de fato mutou o mundo (moveu ou quebrou uma caixa).
    // As jogadas de corpo magnético usam isto pra saber se uma tentativa TRAVADA chegou a mexer
    // em algo — se sim, ainda é um turno de verdade (gravidade + histórico).
    private bool _pushMutated;

    // Frágeis armadas no início do turno atual (peça repousando em cima de cada uma), capturadas
    // junto do Snapshot e conferidas no SettleAndCommit (ver Core.Fragility).
    private List<(Entity Box, Entity Loader)> _fragileLoads = new();

    public void Update(GameWorld session, KeyboardState keyboard)
    {
        // Wrapper fino de input: só a detecção de borda do teclado mora aqui. Toda a regra
        // vive no Step, que também serve chamadores headless (solver, testes).
        (int dx, int dz) = GetFreshDirection(keyboard);
        _previous = keyboard;

        if (dx == 0 && dz == 0)
            return;

        Step(session, dx, dz);
    }

    /// <summary>
    /// Um turno completo do jogo na direção (dx,dz), sem teclado nem render: empurrão,
    /// teleporte, gravidade, placas, histórico e efeitos de célula. É o ponto de entrada
    /// canônico das regras — o input do jogo e o solver/testes (headless) chamam o MESMO
    /// método, então não existe segunda cópia das regras pra divergir. O agendamento de
    /// animação embutido (MarkTeleport/StartTeleportAnims) só escreve componentes
    /// (TeleportAnim) — puro CPU: headless ninguém os consome e o custo é desprezível.
    /// </summary>
    public void Step(GameWorld session, int dx, int dz)
    {
        _world = session;
        _history = session.History;

        // Player caído está congelado: ignora o movimento (só Z/R/T tiram dele desse estado).
        if (session.PlayerFell)
            return;

        if (dx == 0 && dz == 0)
            return;

        // Resolve o player FORA de uma World.Query: o empurrão pode quebrar uma frágil
        // (Remove<Solid>), que é mudança estrutural — proibida durante a iteração de uma query.
        var player = _world.Spatial.First<Player>();
        if (player is null)
            return;

        var facing = _world.World.Get<Facing>(player.Value);
        var pos = _world.World.Get<GridPosition>(player.Value);

        // Caixas magnéticas grudadas (derivado da adjacência, nada é armazenado): com alguma,
        // o player vira corpo rígido e anda em modo tanque; sem nenhuma, movimento livre.
        var magnets = Magnetism.Attached(_world);
        if (magnets.Count > 0)
        {
            bool aligned = (dx == facing.Dx && dz == facing.Dz) || (dx == -facing.Dx && dz == -facing.Dz);
            if (aligned)
                TryMoveBody(player.Value, pos, magnets, dx, dz);
            else
                TryRotateBody(player.Value, pos, facing, magnets, dx, dz);
            return;
        }

        // Livre: o olhar acompanha o comando (mesmo que o passo esbarre e não saia do lugar)
        // e o passo é direto em qualquer direção. O snapshot vem ANTES do olhar mudar, pra o
        // turno gravar o facing anterior — é ele que o undo restaura.
        var before = Snapshot();
        if (!(dx == facing.Dx && dz == facing.Dz))
            _world.World.Set(player.Value, new Facing { Dx = dx, Dz = dz });

        TryMovePlayer(player.Value, pos, dx, dz, before);
    }

    /// <summary>
    /// Undo canônico (a ação do Z): move as peças na direção oposta do último turno, tira o
    /// player do estado caído e re-deriva as placas. Como o Step, é a definição única — o jogo
    /// e o solver/testes (headless) chamam o mesmo método; a parte de render (animação de
    /// teleporte reconstruída) fica por conta do chamador via <see cref="AnimateUndoTeleports"/>.
    /// Retorna false se não havia nada a desfazer.
    /// </summary>
    public bool Undo(GameWorld session)
    {
        if (!session.History.Undo(session))
            return false;

        session.PlayerFell = false;
        PressurePlateSystem.Resolve(session);
        return true;
    }

    private void TryMovePlayer(Entity player, GridPosition pos, int dx, int dz,
        Dictionary<Entity, (GridPosition Pos, bool Solid, Facing? Face)> before)
    {
        int targetX = pos.X + dx;
        int targetZ = pos.Z + dz;

        if (!_world.Grid.IsValid(targetX, pos.Y, targetZ))
            return;

        // Sticky logo atrás do passo: o grude segura o player (não dá pra se afastar dele).
        if (Stickiness.Holds(_world, player, dx, dz))
            return;

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

        SettleAndCommit(player, before);
    }

    /// <summary>
    /// Translação do corpo rígido (player + caixas magnéticas grudadas) na direção alinhada com o
    /// olhar (frente ou ré). O corpo inteiro é erguido do grid primeiro — o movimento é simultâneo
    /// e o destino de uma peça costuma ser a célula que outra está liberando —, então cada peça
    /// resolve sua célula nova com o PushInto (livre, empurrando, quebrando frágil). Qualquer peça
    /// bloqueada trava o corpo inteiro. Portal não passa corpo rígido: um PushInto que redireciona
    /// (landing fora do alvo) conta como bloqueio.
    /// </summary>
    private void TryMoveBody(Entity player, GridPosition pos, List<(Entity Box, int Ox, int Oz)> magnets, int dx, int dz)
    {
        // Qualquer peça do corpo grudada num sticky atrás do passo — ou caixa sobre um trilho
        // que não sai nessa direção — trava a translação inteira (o corpo é rígido). Checado
        // antes de qualquer mutação: jogada impossível, sem turno.
        if (Stickiness.Holds(_world, player, dx, dz))
            return;
        foreach (var (box, _, _) in magnets)
            if (Stickiness.Holds(_world, box, dx, dz) || Rails.Holds(_world, box, dx, dz))
                return;

        var before = Snapshot();
        _teleported.Clear();
        _pushMutated = false;

        _world.Vacate(player);
        foreach (var (box, _, _) in magnets)
            _world.Vacate(box);

        var pieces = new List<(Entity E, GridPosition To)>
        {
            (player, new GridPosition(pos.X + dx, pos.Y, pos.Z + dz)),
        };
        foreach (var (box, ox, oz) in magnets)
            pieces.Add((box, new GridPosition(pos.X + ox + dx, pos.Y, pos.Z + oz + dz)));

        bool ok = true;
        foreach (var (_, to) in pieces)
        {
            var landing = PushInto(to.X, to.Y, to.Z, dx, dz, PlayerPushStrength, new HashSet<Entity>());
            if (landing is null || landing.Value.X != to.X || landing.Value.Y != to.Y || landing.Value.Z != to.Z)
            {
                ok = false;
                break;
            }
        }

        if (!ok)
        {
            // Corpo travado: todo mundo volta a ocupar onde estava. O que uma peça JÁ empurrou
            // antes do bloqueio fica empurrado — se algo mudou, ainda é um turno.
            foreach (var (e, _) in pieces)
                _world.Occupy(e);
            if (_pushMutated)
                SettleAndCommit(player, before);
            return;
        }

        foreach (var (e, to) in pieces)
        {
            _world.World.Set(e, to);
            _world.Occupy(e);
        }

        SettleAndCommit(player, before);
    }

    /// <summary>
    /// Giro do corpo rígido: comando perpendicular ao olhar. O player não muda de célula; cada
    /// caixa grudada varre um quarto de volta ao redor dele — da célula atual até a girada,
    /// passando pela diagonal — empurrando o que estiver no caminho na direção tangente do arco.
    /// Parede ou peça imóvel em qualquer célula varrida trava o giro inteiro (o olhar não muda),
    /// mas o que a varredura JÁ empurrou fica empurrado. Como as caixas do corpo mudam de célula,
    /// um giro bem-sucedido é sempre um turno de verdade (gravidade + histórico).
    /// </summary>
    private void TryRotateBody(Entity player, GridPosition pos, Facing f, List<(Entity Box, int Ox, int Oz)> magnets, int dx, int dz)
    {
        // O quarto de volta que leva o olhar de f até (dx,dz). ccw = (x,z)→(-z,x); senão (z,-x).
        bool ccw = -f.Dz == dx && f.Dx == dz;
        (int X, int Z) Rot(int x, int z) => ccw ? (-z, x) : (z, -x);

        // Giro grudado: o deslocamento líquido de cada caixa varrida (a diagonal do arco) se
        // decompõe nas direções (nx,nz) e (-ox,-oz); um sticky segurando a caixa contra
        // qualquer uma delas trava o giro inteiro, antes de qualquer mutação. Trilho sob a
        // caixa: a varredura sai da célula na tangente (nx,nz) — se o trilho não permite essa
        // direção, o giro também trava.
        foreach (var (box, ox, oz) in magnets)
        {
            var (nx, nz) = Rot(ox, oz);
            if (Stickiness.Holds(_world, box, nx, nz) || Stickiness.Holds(_world, box, -ox, -oz)
                || Rails.Holds(_world, box, nx, nz))
                return;
        }

        var before = Snapshot();
        _teleported.Clear();
        _pushMutated = false;

        // Giro simultâneo: ergue as caixas do corpo antes, pra célula antiga de uma não bloquear
        // a varredura da outra.
        foreach (var (box, _, _) in magnets)
            _world.Vacate(box);

        var landings = new List<(Entity Box, GridPosition To)>();
        bool ok = true;
        foreach (var (box, ox, oz) in magnets)
        {
            var (nx, nz) = Rot(ox, oz);
            int diagX = pos.X + ox + nx, diagZ = pos.Z + oz + nz; // por onde a caixa passa
            int destX = pos.X + nx, destZ = pos.Z + nz;           // onde ela assenta

            // A diagonal é varrida na direção do giro; o destino, pra "trás" do offset antigo.
            // As duas células precisam FICAR livres (redirecionamento de portal não serve).
            var atDiag = PushInto(diagX, pos.Y, diagZ, nx, nz, PlayerPushStrength, new HashSet<Entity>());
            if (atDiag is null || atDiag.Value.X != diagX || atDiag.Value.Y != pos.Y || atDiag.Value.Z != diagZ)
            {
                ok = false;
                break;
            }

            var atDest = PushInto(destX, pos.Y, destZ, -ox, -oz, PlayerPushStrength, new HashSet<Entity>());
            if (atDest is null || atDest.Value.X != destX || atDest.Value.Y != pos.Y || atDest.Value.Z != destZ)
            {
                ok = false;
                break;
            }

            landings.Add((box, new GridPosition(destX, pos.Y, destZ)));
        }

        if (!ok)
        {
            // Giro travado: as caixas voltam a ocupar onde estavam (não chegaram a mudar de
            // célula). O que a varredura já empurrou fica — se algo mudou, ainda é um turno.
            foreach (var (box, _, _) in magnets)
                _world.Occupy(box);
            if (_pushMutated)
                SettleAndCommit(player, before);
            return;
        }

        foreach (var (box, to) in landings)
        {
            _world.World.Set(box, to);
            _world.Occupy(box);
        }
        _world.World.Set(player, new Facing { Dx = dx, Dz = dz });

        SettleAndCommit(player, before);
    }

    /// <summary>
    /// Pós-jogada comum a passo livre, translação e giro do corpo magnético: assenta a gravidade,
    /// re-deriva as placas, dispara animações de teleporte, grava o turno e aplica os efeitos
    /// de célula (placa atemporal, chão-morte).
    /// </summary>
    private void SettleAndCommit(Entity player, Dictionary<Entity, (GridPosition Pos, bool Solid, Facing? Face)> before)
    {
        // Gravidade: tudo que ficou sem apoio cai até pousar — peças empurradas e também as
        // pilhas que perderam o apoio (ex.: caixa em cima do player quando ele sai de baixo).
        Gravity.Settle(_world);

        // Placas de pressão: a ocupação mudou, re-deriva os blocos toggle (e a queda do que
        // repousava sobre um que sumiu). Pode congelar o player (bloco aparecendo nele).
        PressurePlateSystem.Resolve(_world);

        // Frágeis armadas que perderam a carga (quem estava em cima saiu): quebram agora, antes
        // do commit, pra perda de Solid e quedas entrarem no histórico deste turno.
        if (Fragility.ResolveDepartures(_world, _fragileLoads))
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
    /// Posição + solidez + olhar de cada peça reversível (player, caixas, inimigos) no início do
    /// turno. A caixa verde é excluída: nunca empilha (só o R a reverte). Também captura, de
    /// carona, as frágeis armadas do turno (é o único ponto que roda exatamente uma vez por
    /// turno, antes de qualquer mutação).
    /// </summary>
    private Dictionary<Entity, (GridPosition Pos, bool Solid, Facing? Face)> Snapshot()
    {
        _fragileLoads = Fragility.CaptureLoads(_world);

        var snap = new Dictionary<Entity, (GridPosition, bool, Facing?)>();
        var query = new QueryDescription().WithAll<GridPosition, SpawnPosition>();
        _world.World.Query(in query, (Entity e, ref GridPosition p) =>
        {
            if (_world.World.Has<Box>(e) && _world.World.Get<Box>(e).Type == BoxType.Permanent)
                return;
            Facing? face = _world.World.Has<Facing>(e) ? _world.World.Get<Facing>(e) : null;
            snap[e] = (p, _world.World.Has<Solid>(e), face);
        });
        return snap;
    }

    /// <summary>
    /// Grava o deslocamento líquido + mudança de solidez + olhar inicial de cada peça neste turno.
    /// Quem ficou parado grava inércia (delta zero), mantendo as pilhas alinhadas por turno.
    /// </summary>
    private void CommitTurn(Dictionary<Entity, (GridPosition Pos, bool Solid, Facing? Face)> before)
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
            _history.Record(e, new Move(p.X - snap.Pos.X, p.Y - snap.Pos.Y, p.Z - snap.Pos.Z, change, snap.Face));
        }
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
            Entity? partner = FindPartner(_world, occ);
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

        // Caixa com um sticky logo atrás não se afasta dele: o empurrão trava. Nem a frágil
        // quebra — ela está presa pelo grude, não prensada contra algo à frente.
        if (Stickiness.Holds(_world, occ, dx, dz))
            return null;

        // Caixa sobre um trilho só sai dele nas direções permitidas: empurrão fora delas trava.
        // Como no sticky, a frágil não quebra — está presa pelo trilho, não prensada.
        if (Rails.Holds(_world, occ, dx, dz))
            return null;

        // BigBox: duas células como uma unidade só — regra própria, não passa pelo peso genérico.
        if (_world.World.Has<BigBox>(occ))
            return PushBigBox(occ, x, y, z, dx, dz);

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
            _pushMutated = true;
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
    /// Empurra uma <see cref="BigBox"/> (duas células). Ao longo do próprio eixo ela desliza como
    /// uma caixa comum: só a célula além da borda dianteira (na direção do passo) precisa estar
    /// livre — a célula traseira ocupa a que a dianteira libera. Perpendicular ao eixo, as DUAS
    /// células de destino (uma por célula do footprint) precisam estar livres; ao contrário de uma
    /// caixa leve comum, ela não tenta empurrar o que encontrar nelas — trava em vez de encadear
    /// (simplificação deliberada; peso "de graça" mas sem transmitir empurrão). <paramref name="qx"/>
    /// etc. é a célula originalmente consultada por quem chamou <see cref="PushInto"/>, devolvida
    /// livre (como qualquer empurrão bem-sucedido) se a caixa sair do lugar.
    /// </summary>
    private GridPosition? PushBigBox(Entity occ, int qx, int qy, int qz, int dx, int dz)
    {
        var big = _world.World.Get<BigBox>(occ);
        var anchor = _world.World.Get<GridPosition>(occ);
        var (ox, oz) = big.Axis == BigBoxAxis.X ? (1, 0) : (0, 1);
        int secondX = anchor.X + ox, secondZ = anchor.Z + oz;

        bool alongAxis = (ox != 0 && dx != 0) || (oz != 0 && dz != 0);

        if (alongAxis)
        {
            // O offset do eixo é sempre +1, então a borda dianteira na direção positiva é a
            // célula "second"; na negativa, é a própria âncora.
            bool positive = dx > 0 || dz > 0;
            int frontX = positive ? secondX : anchor.X;
            int frontZ = positive ? secondZ : anchor.Z;

            if (_world.Grid.IsOccupied(frontX + dx, anchor.Y, frontZ + dz))
                return null;
        }
        else
        {
            // Deslocamento lateral: as duas células novas nunca sobrepõem o footprint atual.
            if (_world.Grid.IsOccupied(anchor.X + dx, anchor.Y, anchor.Z + dz))
                return null;
            if (_world.Grid.IsOccupied(secondX + dx, anchor.Y, secondZ + dz))
                return null;
        }

        _world.Move(occ, new GridPosition(anchor.X + dx, anchor.Y, anchor.Z + dz));
        _pushMutated = true;
        return new GridPosition(qx, qy, qz);
    }

    /// <summary>
    /// O portal parceiro: o primeiro outro <see cref="PortalBox"/> de mesmo Group, ou null se não
    /// houver par. Com 3+ no mesmo grupo, vence o primeiro da iteração (arranjo não suportado).
    /// </summary>
    private static Entity? FindPartner(GameWorld session, Entity portal)
    {
        int group = session.World.Get<PortalBox>(portal).Group;
        Entity? partner = null;
        var query = new QueryDescription().WithAll<PortalBox>();
        session.World.Query(in query, (Entity e, ref PortalBox pb) =>
        {
            if (partner is null && e != portal && pb.Group == group)
                partner = e;
        });
        return partner;
    }

    /// <summary>
    /// Depois de um undo, reconstrói a animação de teleporte das peças que voltaram atravessando um
    /// portal — sem o histórico ter gravado portal nenhum. Pra cada ação revertida, se o
    /// deslocamento horizontal não é um passo cardinal simples (logo, foi teleporte) e há um portal
    /// vizinho da célula de origem (onde a peça voltou — célula limpa, vale mesmo se ela caiu
    /// depois), monta a <see cref="TeleportAnim"/> com as pontas trocadas: a peça é sugada de volta
    /// pelo portal de SAÍDA e brota no de ENTRADA. Os portais saem todos do mundo (não se movem),
    /// por isso o reverso não precisa tê-los lembrado.
    /// </summary>
    public void AnimateUndoTeleports(GameWorld session, IReadOnlyList<(Entity Entity, Move Move)> reverted)
    {
        foreach (var (e, m) in reverted)
        {
            // Passo cardinal simples (ou parada): desliza normal, sem teleporte.
            if (System.Math.Abs(m.Dx) + System.Math.Abs(m.Dz) <= 1)
                continue;
            if (!session.World.Has<GridPosition>(e) || !session.World.Has<RenderPosition>(e))
                continue;

            var origin = session.World.Get<GridPosition>(e);
            Entity? entryPortal = AdjacentPortal(session, origin);
            if (entryPortal is null)
                continue;
            Entity? exitPortal = FindPartner(session, entryPortal.Value);
            if (exitPortal is null)
                continue;

            var entryCell = session.World.Get<GridPosition>(entryPortal.Value);
            var exitCell = session.World.Get<GridPosition>(exitPortal.Value);
            var anim = new TeleportAnim
            {
                Start = session.World.Get<RenderPosition>(e).Value,
                Entry = GridView.ToWorld(session.Grid, exitCell.X, exitCell.Y, exitCell.Z, GridView.PieceRise),
                Exit = GridView.ToWorld(session.Grid, entryCell.X, entryCell.Y, entryCell.Z, GridView.PieceRise),
                Elapsed = 0f,
            };
            if (session.World.Has<TeleportAnim>(e))
                session.World.Set(e, anim);
            else
                session.World.Add(e, anim);
        }
    }

    /// <summary>O <see cref="PortalBox"/> num dos 4 vizinhos horizontais da célula, ou null.</summary>
    private static Entity? AdjacentPortal(GameWorld session, GridPosition cell)
    {
        return session.Spatial.CellWith<PortalBox>(cell.X + 1, cell.Y, cell.Z)
            ?? session.Spatial.CellWith<PortalBox>(cell.X - 1, cell.Y, cell.Z)
            ?? session.Spatial.CellWith<PortalBox>(cell.X, cell.Y, cell.Z + 1)
            ?? session.Spatial.CellWith<PortalBox>(cell.X, cell.Y, cell.Z - 1);
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
        _pushMutated = true;
    }

    /// <summary>
    /// Direção a partir das teclas recém-pressionadas (uma só por frame), no plano X/Z.
    /// Três layouts equivalentes: WASD, setas e HJKL (vim).
    /// </summary>
    private (int dx, int dz) GetFreshDirection(KeyboardState k)
    {
        if (Pressed(k, Keys.A) || Pressed(k, Keys.Left) || Pressed(k, Keys.H)) return (-1, 0);
        if (Pressed(k, Keys.D) || Pressed(k, Keys.Right) || Pressed(k, Keys.L)) return (1, 0);
        if (Pressed(k, Keys.W) || Pressed(k, Keys.Up) || Pressed(k, Keys.K)) return (0, -1);
        if (Pressed(k, Keys.S) || Pressed(k, Keys.Down) || Pressed(k, Keys.J)) return (0, 1);
        return (0, 0);
    }

    private bool Pressed(KeyboardState current, Keys key)
        => current.IsKeyDown(key) && _previous.IsKeyUp(key);
}
