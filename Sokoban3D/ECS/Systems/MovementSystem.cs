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

        var query = new QueryDescription().WithAll<Player, GridPosition>();
        _world.World.Query(in query, (Entity entity, ref GridPosition pos) =>
        {
            TryMovePlayer(entity, pos, dx, dz);
        });
    }

    private void TryMovePlayer(Entity player, GridPosition pos, int dx, int dz)
    {
        int targetX = pos.X + dx;
        int targetZ = pos.Z + dz;

        if (!_world.Grid.IsValid(targetX, pos.Y, targetZ))
            return;

        var record = new List<EntityState>();

        if (_world.Grid.IsOccupied(targetX, pos.Y, targetZ))
        {
            // Só avança se a célula alvo puder ser liberada (empurrando ou quebrando).
            if (!TryClear(targetX, pos.Y, targetZ, dx, dz, PlayerPushStrength, record))
                return;
        }

        record.Add(new EntityState(player, pos, null));
        _world.Grid.SetOccupied(pos.X, pos.Y, pos.Z, false);
        _world.Grid.SetOccupied(targetX, pos.Y, targetZ, true);
        _world.World.Set(player, new GridPosition(targetX, pos.Y, targetZ));

        // Gravidade: toda peça que se moveu (player + caixas empurradas) cai até pousar.
        // A queda faz parte da MESMA ação no histórico — as posições anteriores já estão
        // gravadas em `record`, então um único Z reverte movimento + queda.
        ApplyGravity(record);

        _history.Push(record);

        // Player que repousou em y==0 está apoiado só pelo chão-morte: caiu, congela.
        var landed = _world.World.Get<GridPosition>(player);
        if (landed.Y == 0)
            _world.PlayerFell = true;
    }

    /// <summary>
    /// Aplica gravidade a cada peça que ocupa célula dentre as afetadas pela ação: ela
    /// desce enquanto a célula logo abaixo estiver vazia, parando ao encontrar obstáculo,
    /// caixa ou o limite inferior do grid (o chão-morte). Processa de baixo pra cima pra
    /// que apoios assentem antes do que está sobre eles.
    /// </summary>
    private void ApplyGravity(List<EntityState> affected)
    {
        var movers = new List<Entity>();
        foreach (var s in affected)
            if (Occupies(s.Entity) && !movers.Contains(s.Entity))
                movers.Add(s.Entity);

        movers.Sort((a, b) =>
            _world.World.Get<GridPosition>(a).Y.CompareTo(_world.World.Get<GridPosition>(b).Y));

        foreach (var e in movers)
        {
            var pos = _world.World.Get<GridPosition>(e);
            int ny = pos.Y;
            while (!_world.Grid.IsOccupied(pos.X, ny - 1, pos.Z))
                ny--;

            if (ny == pos.Y)
                continue;

            _world.Grid.SetOccupied(pos.X, pos.Y, pos.Z, false);
            _world.Grid.SetOccupied(pos.X, ny, pos.Z, true);
            _world.World.Set(e, new GridPosition(pos.X, ny, pos.Z));
        }
    }

    /// <summary>Uma peça ocupa célula se for o player ou uma caixa não-quebrada.</summary>
    private bool Occupies(Entity e)
    {
        if (_world.World.Has<Player>(e))
            return true;
        if (_world.World.Has<Box>(e))
            return !_world.World.Get<Box>(e).Broken;
        return false;
    }

    /// <summary>
    /// Tenta liberar a célula (x,y,z) na direção (dx,dz). Retorna true se a célula ficou
    /// livre (estava vazia, a caixa foi empurrada, ou a caixa frágil quebrou).
    /// Só causa mutações nos caminhos que terminam em sucesso.
    /// </summary>
    private bool TryClear(int x, int y, int z, int dx, int dz, int budget, List<EntityState> record)
    {
        if (!_world.Grid.IsValid(x, y, z))
            return false; // parede

        if (!_world.Grid.IsOccupied(x, y, z))
            return true; // já está livre

        Entity? boxEntity = FindBoxAt(x, y, z);
        if (boxEntity is null)
            return false; // ocupado por algo que não é caixa

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
        bool aheadClear = TryClear(aheadX, y, aheadZ, dx, dz, forwardBudget, record);

        if (aheadClear)
        {
            // Empurra a caixa pra frente. Caixas imunes ao undo movem normalmente,
            // mas não são registradas — o undo não as reverte (só o R).
            if (!BoxRules.IgnoresUndo(box.Type))
                record.Add(new EntityState(boxEntity.Value, new GridPosition(x, y, z), box));
            _world.Grid.SetOccupied(x, y, z, false);
            _world.Grid.SetOccupied(aheadX, y, aheadZ, true);
            _world.World.Set(boxEntity.Value, new GridPosition(aheadX, y, aheadZ));
            return true;
        }

        // Não dá pra avançar. Frágil quebra (e libera a célula); o resto fica travado.
        if (box.Type == BoxType.Fragile)
        {
            record.Add(new EntityState(boxEntity.Value, new GridPosition(x, y, z), box));
            BreakBox(boxEntity.Value);
            return true;
        }

        return false;
    }

    private void BreakBox(Entity entity)
    {
        var p = _world.World.Get<GridPosition>(entity);
        _world.Grid.SetOccupied(p.X, p.Y, p.Z, false);

        var box = _world.World.Get<Box>(entity);
        box.Broken = true;
        _world.World.Set(entity, box);
    }

    private Entity? FindBoxAt(int x, int y, int z)
    {
        Entity? found = null;
        var query = new QueryDescription().WithAll<Box, GridPosition>();
        _world.World.Query(in query, (Entity entity, ref Box b, ref GridPosition p) =>
        {
            if (!b.Broken && p.X == x && p.Y == y && p.Z == z)
                found = entity;
        });
        return found;
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
