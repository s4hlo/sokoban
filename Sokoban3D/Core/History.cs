using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Core;

/// <summary>
/// Mudança de solidez que um movimento causou, pra o undo desfazer na direção oposta.
/// Quase todo movimento é só deslocamento (<see cref="None"/>); os casos com tag <see cref="Solid"/>
/// são a frágil que quebra (<see cref="Lost"/>) e a caixa quebrada que o restart re-solidifica
/// (<see cref="Gained"/>).
/// </summary>
public enum SolidChange : byte
{
    None,   // não mexeu no tag Solid
    Lost,   // TIROU o Solid (frágil quebrou). Undo readiciona.
    Gained, // ADICIONOU o Solid (restart re-solidifica). Undo remove.
}

/// <summary>
/// O que UMA peça fez num turno: o deslocamento líquido <c>(Dx,Dy,Dz)</c> (movimento + queda
/// somados) e a mudança de solidez. Deslocamento zero e <see cref="SolidChange.None"/> é a
/// "inércia" — a peça não fez nada naquele turno, mas grava mesmo assim pra manter as pilhas de
/// todas as peças alinhadas (mesma altura = mesmo turno no topo). O undo desfaz movendo por
/// <c>-(delta)</c> e invertendo a solidez.
/// </summary>
public readonly struct Move
{
    public readonly int Dx;
    public readonly int Dy;
    public readonly int Dz;
    public readonly SolidChange Solid;

    public Move(int dx, int dy, int dz, SolidChange solid = SolidChange.None)
    {
        Dx = dx;
        Dy = dy;
        Dz = dz;
        Solid = solid;
    }
}

/// <summary>
/// Histórico de undo: uma pilha de movimentos POR PEÇA. Cada turno, toda peça com histórico
/// empilha um <see cref="Move"/> (o que fez, ou inércia se ficou parada) — então as pilhas crescem
/// juntas e ficam alinhadas por turno. Um Z popa o topo de CADA pilha e executa o movimento
/// oposto. A caixa verde (<see cref="BoxType.Permanent"/>) simplesmente nunca empilha; a placa
/// atemporal (<see cref="TimelessBase"/>) limpa a pilha de quem pisa nela via <see cref="Forget"/>.
///
/// O histórico só conhece MOVIMENTO de peça. Estado derivado (a solidez dos blocos
/// <see cref="Toggle"/>, função das placas) não passa por aqui — quem o reconstrói é o
/// <see cref="Systems.PressurePlateSystem"/> no fluxo normal, depois que o undo restaura posições.
/// </summary>
public class History
{
    private readonly Dictionary<Entity, Stack<Move>> _stacks = new();

    /// <summary>Empilha o movimento do turno na pilha da peça (criando a pilha na primeira vez).</summary>
    public void Record(Entity entity, Move move)
    {
        if (!_stacks.TryGetValue(entity, out var stack))
        {
            stack = new Stack<Move>();
            _stacks[entity] = stack;
        }
        stack.Push(move);
    }

    public void Clear() => _stacks.Clear();

    /// <summary>
    /// Esvazia a pilha de uma peça: ela fica "commitada" na posição atual e o undo não a reverte
    /// mais (até onde foi limpa). É o que a placa atemporal faz com quem pisa nela. Como a peça
    /// continua empilhando nos turnos seguintes, o topo dela permanece alinhado com o turno atual;
    /// só o passado profundo some.
    /// </summary>
    public void Forget(Entity entity)
    {
        if (_stacks.TryGetValue(entity, out var stack))
            stack.Clear();
    }

    /// <summary>
    /// Desfaz o último turno: popa o topo da pilha de cada peça e a move por <c>-(delta)</c>
    /// (invertendo a solidez). Peças com pilha vazia ficam paradas (já estão commitadas). Move
    /// como movimento normal — se a célula de volta estiver ocupada por algo que não foi revertido,
    /// a peça fica onde está. Retorna false se nada havia pra desfazer.
    /// </summary>
    public bool Undo(GameWorld world)
    {
        bool undone = false;
        foreach (var (entity, stack) in _stacks)
        {
            if (stack.Count == 0)
                continue;

            undone = true;
            ApplyReverse(world, entity, stack.Pop());
        }
        return undone;
    }

    private static void ApplyReverse(GameWorld world, Entity entity, Move m)
    {
        var cur = world.World.Get<GridPosition>(entity);
        var target = new GridPosition(cur.X - m.Dx, cur.Y - m.Dy, cur.Z - m.Dz);

        switch (m.Solid)
        {
            case SolidChange.Lost:
                // O turno tirou o Solid (frágil quebrou): readiciona e reocupa a célula.
                world.World.Set(entity, target);
                world.World.Add(entity, new Solid());
                world.Occupy(entity);
                break;

            case SolidChange.Gained:
                // O turno adicionou o Solid (restart re-solidificou): desocupa, tira o Solid e
                // volta a posição sem reocupar (fica quebrada de novo).
                world.Vacate(entity);
                world.World.Remove<Solid>(entity);
                world.World.Set(entity, target);
                break;

            default:
                // Movimento puro. Peça que não ocupa o grid: só a posição lógica.
                if (!world.World.Has<Solid>(entity))
                {
                    world.World.Set(entity, target);
                    break;
                }
                // Move respeitando a ocupação: libera a célula atual e só ocupa a de volta se ela
                // estiver livre; senão fica parada.
                world.Vacate(entity);
                if (world.Grid.IsValid(target.X, target.Y, target.Z)
                    && !world.Grid.IsOccupied(target.X, target.Y, target.Z))
                {
                    world.World.Set(entity, target);
                    world.Occupy(entity);
                }
                else
                {
                    world.Occupy(entity);
                }
                break;
        }
    }
}
