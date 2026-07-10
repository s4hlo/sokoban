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
/// O que uma peça fez num turno: deslocamento líquido <c>(Dx,Dy,Dz)</c> (movimento + queda), a
/// mudança de solidez e o olhar do INÍCIO do turno (<see cref="Face"/>, null pra peça sem
/// <see cref="Facing"/>). Tudo zero é inércia — ficou parada, mas grava mesmo assim pra manter as
/// pilhas de todas as peças alinhadas por turno. O reverso anda por <c>-(delta)</c>, inverte a
/// solidez e restaura o olhar.
/// </summary>
public readonly struct Move
{
    public readonly int Dx;
    public readonly int Dy;
    public readonly int Dz;
    public readonly SolidChange Solid;
    public readonly Facing? Face;

    public Move(int dx, int dy, int dz, SolidChange solid = SolidChange.None, Facing? face = null)
    {
        Dx = dx;
        Dy = dy;
        Dz = dz;
        Solid = solid;
        Face = face;
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

    // Ações revertidas no último Undo (peça + delta). Exposto pra quem quiser reagir ao reverso
    // sem o histórico precisar saber o PORQUÊ de cada ação — ex.: a animação de teleporte, que
    // descobre os portais consultando o mundo a partir daqui. Reusado a cada Undo.
    private readonly List<(Entity Entity, Move Move)> _lastReverted = new();
    public IReadOnlyList<(Entity Entity, Move Move)> LastReverted => _lastReverted;

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

        _lastReverted.Clear();
        _lastReverted.AddRange(moves);

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

        // Inverte a solidez que o turno mexeu (frágil que quebrou / restart que re-solidificou).
        if (m.Solid == SolidChange.Lost) world.World.Add(entity, new Solid());
        if (m.Solid == SolidChange.Gained) world.World.Remove<Solid>(entity);

        // Restaura o olhar do início do turno (gravado só pra quem tem Facing). A posição pode
        // esbarrar e ficar (abaixo), mas o olhar volta sempre — rotação não colide com nada.
        if (m.Face is { } face)
            world.World.Set(entity, face);

        // Não ocupa o grid: só a posição lógica.
        if (!world.World.Has<Solid>(entity))
        {
            world.World.Set(entity, target);
            return;
        }

        // Sólida (já vacada no lote): anda por -delta se o destino estiver livre; se tiver algo no
        // caminho (terreno, caixa verde, peça commitada numa placa atemporal), fica onde está — igual
        // a um passo normal que esbarra. Ocupa onde parar.
        if (world.FootprintFree(entity, target.X, target.Y, target.Z))
            world.World.Set(entity, target);
        world.Occupy(entity);
    }
}
