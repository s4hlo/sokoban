using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.ECS.Components;
using Sokoban3D.ECS.Systems;

namespace Sokoban3D.Core;

/// <summary>
/// Mudança de solidez que um passo causou no grid, pra o undo desfazer na direção oposta.
/// Quase todo passo é só movimento (<see cref="None"/>); os dois casos com mudança de tag
/// <see cref="Solid"/> são a frágil que quebra e a caixa quebrada que o restart re-solidifica.
/// </summary>
public enum SolidChange : byte
{
    None,   // o passo não mexeu no tag Solid
    Lost,   // o passo TIROU o Solid (frágil quebrou). Undo readiciona o Solid.
    Gained, // o passo ADICIONOU o Solid (restart re-solidifica uma quebrada). Undo remove o Solid.
}

/// <summary>
/// Um passo atômico de uma ação: o MOVIMENTO de UMA peça — o deslocamento <c>(Dx,Dy,Dz)</c> —
/// e a mudança de solidez que ele causou. O undo desfaz o passo: move a peça por <c>-(delta)</c>
/// e inverte a solidez. Guardar MOVIMENTO (e não posição absoluta) é o que deixa o undo ser um
/// replay reverso simples: cada passo é desfeito relativo à posição atual, então peças fora do
/// histórico (caixa verde, peça commitada numa placa atemporal) não precisam ser reconciliadas —
/// elas simplesmente não têm passos pra desfazer.
/// </summary>
public readonly struct MoveStep
{
    public readonly Entity Entity;
    public readonly int Dx;
    public readonly int Dy;
    public readonly int Dz;
    public readonly SolidChange Solid;

    public MoveStep(Entity entity, int dx, int dy, int dz, SolidChange solid = SolidChange.None)
    {
        Entity = entity;
        Dx = dx;
        Dy = dy;
        Dz = dz;
        Solid = solid;
    }

    /// <summary>Passo de movimento puro: a peça foi de <paramref name="from"/> para <paramref name="to"/>.</summary>
    public static MoveStep Moved(Entity e, GridPosition from, GridPosition to)
        => new(e, to.X - from.X, to.Y - from.Y, to.Z - from.Z);

    /// <summary>Passo de quebra: a peça (frágil) perdeu o Solid sem se mover.</summary>
    public static MoveStep Broke(Entity e) => new(e, 0, 0, 0, SolidChange.Lost);
}

/// <summary>
/// Histórico de ações pra desfazer (undo). Cada ação é a lista ordenada dos passos (movimentos)
/// que aconteceram naquele turno — player, caixas empurradas, quedas por gravidade, quebras de
/// frágil. A caixa verde nunca entra. Um Z desfaz a última ação inteira, em replay reverso.
/// </summary>
public class History
{
    private readonly Stack<List<MoveStep>> _stack = new();

    public void Push(List<MoveStep> steps) => _stack.Push(steps);

    public void Clear() => _stack.Clear();

    /// <summary>
    /// Apaga TODO o histórico passado de uma peça: remove os passos dela de todas as ações da
    /// pilha (ações que ficam vazias são descartadas). A peça nunca mais é revertida por um Z
    /// futuro — fica "commitada" na posição atual. É isso que a placa atemporal
    /// (<see cref="TimelessBase"/>) faz com quem pisa nela.
    ///
    /// É um expurgo único do passado, NÃO um flag persistente: a peça não ganha marca nenhuma,
    /// então assim que ela voltar a se mover (ex.: sair da placa) o próximo passo já é gravado de
    /// novo. Difere da caixa verde (<see cref="BoxType.Permanent"/>), que ignora o undo pra sempre.
    /// </summary>
    public void Forget(Entity entity)
    {
        if (_stack.Count == 0)
            return;

        // Stack não dá pra editar no meio: despeja num array (topo→base), filtra e repõe.
        var actions = _stack.ToArray(); // [0] = topo
        _stack.Clear();
        for (int i = actions.Length - 1; i >= 0; i--) // base→topo, pra repor a ordem original
        {
            var action = actions[i];
            action.RemoveAll(s => s.Entity == entity);
            if (action.Count > 0)
                _stack.Push(action);
        }
    }

    /// <summary>
    /// Desfaz a última ação. Replay reverso: cada passo é desfeito de trás pra frente, movendo a
    /// peça por -(delta) como um movimento NORMAL — libera a célula atual e ocupa a de volta só se
    /// estiver livre. Se algo fora do histórico (caixa verde, peça commitada numa placa) estiver
    /// na célula de volta, o movimento é bloqueado e a peça fica onde está — exatamente como
    /// qualquer movimento contra célula ocupada, sem tratamento especial. No fim re-deriva os
    /// toggles pelas placas. Retorna false se não há o que desfazer.
    /// </summary>
    public bool Undo(GameWorld world)
    {
        if (_stack.Count == 0)
            return false;

        var steps = _stack.Pop();

        for (int i = steps.Count - 1; i >= 0; i--)
        {
            var s = steps[i];
            var cur = world.World.Get<GridPosition>(s.Entity);
            var target = new GridPosition(cur.X - s.Dx, cur.Y - s.Dy, cur.Z - s.Dz);

            switch (s.Solid)
            {
                case SolidChange.Lost:
                    // O passo tirou o Solid (frágil quebrou, sem mover): readiciona e reocupa a
                    // própria célula (livre nesta altura do replay).
                    world.World.Set(s.Entity, target);
                    world.World.Add(s.Entity, new Solid());
                    world.Occupy(s.Entity);
                    break;

                case SolidChange.Gained:
                    // O passo adicionou o Solid (restart re-solidificou) e moveu: desocupa, tira o
                    // Solid e volta a posição sem reocupar (fica quebrada de novo).
                    world.Vacate(s.Entity);
                    world.World.Remove<Solid>(s.Entity);
                    world.World.Set(s.Entity, target);
                    break;

                default:
                    // Movimento puro. Peça que não ocupa o grid: só a posição lógica.
                    if (!world.World.Has<Solid>(s.Entity))
                    {
                        world.World.Set(s.Entity, target);
                        break;
                    }
                    // Move respeitando a ocupação (regra normal de qualquer movimento): libera a
                    // célula atual e só ocupa a de volta se ela estiver livre; senão fica parada.
                    world.Vacate(s.Entity);
                    if (world.Grid.IsValid(target.X, target.Y, target.Z)
                        && !world.Grid.IsOccupied(target.X, target.Y, target.Z))
                    {
                        world.World.Set(s.Entity, target);
                        world.Occupy(s.Entity);
                    }
                    else
                    {
                        world.Occupy(s.Entity); // bloqueado: reocupa a célula atual
                    }
                    break;
            }
        }

        // Toggles são estado derivado (nunca no histórico): re-deriva pelas placas no estado
        // restaurado. Record descartável — nada daqui é gravado.
        PressurePlateSystem.Resolve(world, new List<MoveStep>());

        return true;
    }
}
