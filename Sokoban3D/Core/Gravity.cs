using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Core;

/// <summary>
/// Gravidade compartilhada: cada peça desce enquanto a célula logo abaixo estiver vazia,
/// parando ao encontrar obstáculo, caixa ou o limite inferior do grid (o chão-morte).
/// Processa de baixo pra cima pra que apoios assentem antes do que está sobre eles.
///
/// É usada tanto pelo empurrão do player (<see cref="ECS.Systems.MovementSystem"/>) quanto
/// pelo bloco que some sob uma peça (<see cref="ECS.Systems.PressurePlateSystem"/>). Cada queda
/// vira um passo de movimento próprio no <paramref name="record"/> da ação (deslocamento só no
/// eixo Y), pra um único undo desfazer movimento + queda no mesmo replay reverso.
/// </summary>
public static class Gravity
{
    public static void Apply(GameWorld world, List<Entity> movers, List<MoveStep> record)
    {
        // Assenta de baixo pra cima: o apoio pousa antes do que repousa sobre ele.
        movers.Sort((a, b) =>
            world.World.Get<GridPosition>(a).Y.CompareTo(world.World.Get<GridPosition>(b).Y));

        foreach (var e in movers)
        {
            if (!world.World.Has<Solid>(e))
                continue; // peça que deixou de ocupar o grid (ex.: frágil quebrada) não cai

            var pos = world.World.Get<GridPosition>(e);
            int ny = pos.Y;
            while (!world.Grid.IsOccupied(pos.X, ny - 1, pos.Z))
                ny--;

            if (ny == pos.Y)
                continue;

            var to = new GridPosition(pos.X, ny, pos.Z);
            record.Add(MoveStep.Moved(e, pos, to));
            world.Move(e, to);
        }
    }
}
