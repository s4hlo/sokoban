using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Core;

/// <summary>
/// Estado anterior de uma peça afetada por uma ação: a posição e se ela ocupava o grid
/// (<see cref="WasSolid"/>) no momento do registro. Guardar a ocupação — não só a posição —
/// deixa o undo reverter mudanças como uma caixa frágil que quebrou (readicionando o Solid).
/// </summary>
public readonly struct EntityState
{
    public readonly Entity Entity;
    public readonly GridPosition Position;
    public readonly bool WasSolid;

    public EntityState(Entity entity, GridPosition position, bool wasSolid)
    {
        Entity = entity;
        Position = position;
        WasSolid = wasSolid;
    }
}

/// <summary>
/// Histórico de ações pra desfazer (undo). Cada ação é o conjunto de peças afetadas
/// (player + caixas empurradas/quebradas) com seu estado anterior.
/// </summary>
public class History
{
    private readonly Stack<List<EntityState>> _stack = new();

    public void Push(List<EntityState> states) => _stack.Push(states);

    public void Clear() => _stack.Clear();

    /// <summary>
    /// Desfaz a última ação, restaurando estado e posição de cada peça e corrigindo o grid.
    /// Retorna false se não há o que desfazer.
    /// </summary>
    public bool Undo(GameWorld world)
    {
        if (_stack.Count == 0)
            return false;

        var states = _stack.Pop();

        // 1) Restaura a ocupação (presença do Solid) antes da posição, pra que Occupies já
        //    enxergue o estado restaurado (ex.: frágil que volta inteira reocupa o grid).
        //    Mudança estrutural segura: aqui se itera uma List, não uma World.Query.
        foreach (var s in states)
        {
            bool isSolid = world.World.Has<Solid>(s.Entity);
            if (s.WasSolid && !isSolid)
                world.World.Add(s.Entity, new Solid());
            else if (!s.WasSolid && isSolid)
                world.World.Remove<Solid>(s.Entity);
        }

        // 2) Restaura a posição peça-a-peça, de trás pra frente, liberando a célula só
        //    na hora de mover cada peça. Essa ordem (o "empurrador" antes do empurrado)
        //    garante que o alvo já foi desocupado por quem estava atrás — então o grid
        //    nunca "mente" durante a volta, e o único ocupante possível do alvo é a verde
        //    (que nunca está no histórico). Nesse caso a peça empurra a verde em cadeia;
        //    o player conta como leve e também é empurrado. Se não der pra liberar, fica.
        for (int i = states.Count - 1; i >= 0; i--)
        {
            var s = states[i];
            var current = world.World.Get<GridPosition>(s.Entity);
            var target = s.Position;

            // Peça que não ocupa célula (caixa quebrada): só restaura a posição lógica.
            if (!Occupies(world, s.Entity))
            {
                world.World.Set(s.Entity, target);
                continue;
            }

            // Libera a célula atual antes de tentar a volta (a peça vai sair dela).
            world.Grid.SetOccupied(current.X, current.Y, current.Z, false);

            int dx = target.X - current.X;
            int dy = target.Y - current.Y;
            int dz = target.Z - current.Z;

            if (!TryClearForUndo(world, target.X, target.Y, target.Z, dx, dy, dz))
            {
                // Não deu pra liberar o alvo: a peça fica onde estava.
                world.Grid.SetOccupied(current.X, current.Y, current.Z, true);
                continue;
            }

            world.World.Set(s.Entity, target);
            world.Grid.SetOccupied(target.X, target.Y, target.Z, true);
        }

        return true;
    }

    /// <summary>
    /// Libera a célula (x,y,z) na direção (dx,dy,dz) durante o undo, empurrando em cadeia
    /// o que estiver no caminho. São empurráveis a caixa verde (Permanent) e o player (tratado
    /// como peça leve); caixas normais e inimigos bloqueiam. Nada disso é gravado no histórico —
    /// é só efeito físico da peça que está voltando. Retorna true se a célula ficou livre.
    /// </summary>
    private static bool TryClearForUndo(GameWorld world, int x, int y, int z, int dx, int dy, int dz)
    {
        if (!world.Grid.IsValid(x, y, z))
            return false; // parede
        if (!world.Grid.IsOccupied(x, y, z))
            return true; // já livre

        Entity? occupant = world.Spatial.Occupant(x, y, z);
        if (occupant is null)
            return false; // ocupado por algo sem entidade conhecida: por segurança, bloqueia

        Entity e = occupant.Value;
        // Só a verde é empurrável no undo. O player bloqueia de propósito: empurrá-lo
        // dessincronizaria a posição dele do próprio histórico de movimentos. Caixa
        // normal e inimigo também bloqueiam.
        bool pushable = world.World.Has<Box>(e) && world.World.Get<Box>(e).Type == BoxType.Permanent;
        if (!pushable)
            return false;

        int ax = x + dx;
        int ay = y + dy;
        int az = z + dz;

        if (!TryClearForUndo(world, ax, ay, az, dx, dy, dz))
            return false;

        world.Grid.SetOccupied(x, y, z, false);
        world.Grid.SetOccupied(ax, ay, az, true);
        world.World.Set(e, new GridPosition(ax, ay, az));
        return true;
    }

    /// <summary>Uma peça ocupa célula se tem o tag <see cref="Solid"/>.</summary>
    private static bool Occupies(GameWorld world, Entity entity)
        => world.World.Has<Solid>(entity);
}
