using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Core;

/// <summary>
/// Mudança de solidez de um movimento, pra o reverso desfazer junto. <see cref="Lost"/> é a frágil
/// que quebrou; <see cref="Gained"/> a caixa quebrada que o restart re-solidifica.
/// </summary>
public enum SolidChange : byte
{
    None,   // não mexeu no tag Solid
    Lost,   // TIROU o Solid (frágil quebrou). Reverso readiciona.
    Gained, // ADICIONOU o Solid (restart re-solidifica). Reverso remove.
}

/// <summary>
/// O que uma peça fez num turno: deslocamento líquido <c>(Dx,Dy,Dz)</c> (movimento + queda) e a
/// mudança de solidez. Tudo zero é inércia — ficou parada, mas grava mesmo assim pra manter as
/// pilhas de todas as peças alinhadas por turno. O reverso anda por <c>-(delta)</c> e inverte a solidez.
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
/// Pilha de movimentos por peça. Cada turno toda peça empilha um <see cref="Move"/> (ou inércia),
/// então as pilhas ficam alinhadas por turno; <see cref="Undo"/> popa o topo de cada uma e anda na
/// direção oposta. A caixa verde (<see cref="BoxType.Permanent"/>) nunca empilha; a placa atemporal
/// limpa a pilha de quem pisa nela (<see cref="Forget"/>). Estado derivado (solidez dos
/// <see cref="Toggle"/>) fica fora daqui — o <see cref="Systems.PressurePlateSystem"/> o re-deriva
/// depois que as posições voltam.
/// </summary>
public class History
{
    private readonly Dictionary<Entity, Stack<Move>> _stacks = new();

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
    /// Esvazia a pilha de uma peça: ela fica commitada na posição atual e o reverso não a toca mais.
    /// É o efeito da placa atemporal. Como segue empilhando nos turnos seguintes, o topo continua
    /// alinhado — só o passado some.
    /// </summary>
    public void Forget(Entity entity)
    {
        if (_stacks.TryGetValue(entity, out var stack))
            stack.Clear();
    }

    /// <summary>
    /// Move cada peça na direção oposta do último turno: popa o topo de cada pilha e anda por
    /// <c>-(delta)</c>, invertendo a solidez. Pilha vazia fica parada. Retorna false se não havia
    /// nada a desfazer.
    /// </summary>
    public bool Undo(GameWorld world)
    {
        // Popa o topo de todas as pilhas de uma vez — o reverso é simultâneo (ver Vacate em lote abaixo).
        var moves = _stacks
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => (Entity: kv.Key, Move: kv.Value.Pop()))
            .ToList();

        if (moves.Count == 0)
            return false;

        // Ergue do grid toda peça sólida do lote ANTES de reposicionar qualquer uma. O reverso é
        // simultâneo: o destino de uma peça é a célula que a peça à frente está liberando agora.
        // Vacando o lote inteiro, cada destino fica livre e a colisão restante (terreno, peças
        // esquecidas pela placa atemporal) independe da ordem de iteração.
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
                // Frágil que quebrou: não foi vacada (estava sem Solid). Volta, re-solidifica e ocupa.
                world.World.Set(entity, target);
                world.World.Add(entity, new Solid());
                world.Occupy(entity);
                break;

            case SolidChange.Gained:
                // Restart re-solidificou: já foi vacada. Volta sem reocupar — fica quebrada de novo.
                world.World.Remove<Solid>(entity);
                world.World.Set(entity, target);
                break;

            default:
                // Peça que não ocupa o grid: só a posição lógica.
                if (!world.World.Has<Solid>(entity))
                {
                    world.World.Set(entity, target);
                    break;
                }
                // Sólida (já vacada): volta se o destino estiver livre — nesse ponto só pode estar
                // ocupado por peça fora do lote. Senão fica onde está.
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
