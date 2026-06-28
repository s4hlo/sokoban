using System.Collections.Generic;
using Arch.Core;
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

        if (_world.Grid.IsOccupied(targetX, pos.Y, targetZ))
        {
            // Só avança se a célula alvo puder ser liberada (empurrando ou quebrando). Falhou:
            // não houve turno, o snapshot é descartado.
            if (!TryClear(targetX, pos.Y, targetZ, dx, dz, PlayerPushStrength))
                return;
        }

        var playerTo = new GridPosition(targetX, pos.Y, targetZ);
        _world.Move(player, playerTo);

        // Gravidade: toda peça que se moveu (player + caixas empurradas) cai até pousar.
        Gravity.Apply(_world, MovedSince(before));

        // Placas de pressão: a ocupação mudou, re-deriva os blocos toggle (e a queda do que
        // repousava sobre um que sumiu). Pode congelar o player (bloco aparecendo nele).
        PressurePlateSystem.Resolve(_world);

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
    /// Tenta liberar a célula (x,y,z) na direção (dx,dz). Retorna true se a célula ficou
    /// livre (estava vazia, a caixa foi empurrada, ou a caixa frágil quebrou).
    /// Só causa mutações nos caminhos que terminam em sucesso.
    /// </summary>
    private bool TryClear(int x, int y, int z, int dx, int dz, int budget)
    {
        if (!_world.Grid.IsValid(x, y, z))
            return false; // parede

        if (!_world.Grid.IsOccupied(x, y, z))
            return true; // já está livre

        Entity? occupant = _world.Grid.Occupant(x, y, z);
        if (occupant is null || !_world.World.Has<Box>(occupant.Value))
            return false; // ocupado por algo que não é caixa (player, inimigo, obstáculo)
        Entity? boxEntity = occupant;

        var box = _world.World.Get<Box>(boxEntity.Value);
        int weight = BoxRules.Weight(box.Type);
        if (weight > budget)
            return false; // pesada demais pra força restante: não sai do lugar

        int aheadX = x + dx;
        int aheadZ = z + dz;

        // Caixa sem peso (leve/frágil) não transmite força: passa orçamento 0 pra frente,
        // então não empurra nada com peso. Prensada contra média/pesada, a frágil quebra
        // e a leve só trava (ver abaixo). As com peso repassam a força que sobra.
        int forwardBudget = weight == 0 ? 0 : budget - weight;
        bool aheadClear = TryClear(aheadX, y, aheadZ, dx, dz, forwardBudget);

        if (aheadClear)
        {
            // Empurra a caixa pra frente. A caixa verde move normalmente; o CommitTurn não a grava
            // (foi excluída do snapshot), então o undo não a reverte — só o R.
            var to = new GridPosition(aheadX, y, aheadZ);
            _world.Move(boxEntity.Value, to);
            return true;
        }

        // Não dá pra avançar. Frágil quebra (e libera a célula); o resto fica travado.
        if (box.Type == BoxType.Fragile)
        {
            BreakBox(boxEntity.Value);
            return true;
        }

        return false;
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
