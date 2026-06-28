using System.Collections.Generic;
using System.Linq;
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
        // Popa o topo de cada pilha de uma vez. Reverter peça a peça com checagem de ocupação
        // dependia da ordem de iteração: numa cadeia empurrada (player → caixa A → caixa B) a
        // célula de destino de uma peça é a célula ATUAL da peça à frente, então uma peça revertida
        // cedo demais via o destino ainda ocupado e ficava parada — undo inconsistente.
        var moves = _stacks
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => (Entity: kv.Key, Move: kv.Value.Pop()))
            .ToList();

        if (moves.Count == 0)
            return false;

        // Tira do grid TODA peça sólida do lote ANTES de reposicionar qualquer uma. Como o estado
        // pós-undo é uma configuração que já foi válida, depois de erguer o lote inteiro o destino
        // de cada peça está livre. As únicas células ainda ocupadas são de peças FORA do lote
        // (terreno, ou peças "esquecidas" por placa atemporal) — é só contra essas que a checagem
        // de ocupação abaixo precisa proteger, e agora ela independe da ordem.
        foreach (var (entity, _) in moves)
            if (world.World.Has<Solid>(entity))
                world.Vacate(entity);

        foreach (var (entity, move) in moves)
            ApplyReverse(world, entity, move);

        return true;
    }

    private static void ApplyReverse(GameWorld world, Entity entity, Move m)
    {
        var cur = world.World.Get<GridPosition>(entity);
        var target = new GridPosition(cur.X - m.Dx, cur.Y - m.Dy, cur.Z - m.Dz);

        switch (m.Solid)
        {
            case SolidChange.Lost:
                // O turno tirou o Solid (frágil quebrou): estava não-sólida (não foi vacada acima),
                // readiciona o Solid e ocupa a célula de volta.
                world.World.Set(entity, target);
                world.World.Add(entity, new Solid());
                world.Occupy(entity);
                break;

            case SolidChange.Gained:
                // O turno adicionou o Solid (restart re-solidificou): já foi vacada acima; tira o
                // Solid e volta a posição sem reocupar (fica quebrada de novo).
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
                // Sólida (já vacada acima): volta ao destino se ele estiver livre — nesse ponto só
                // pode estar ocupado por peça fora do lote. Senão, fica onde estava (reocupa).
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
