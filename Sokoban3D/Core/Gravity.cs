using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Core;

/// <summary>
/// Gravidade compartilhada: "se der pra cair, cai". Assenta TODAS as peças móveis do mundo —
/// cada uma desce enquanto a célula logo abaixo estiver vazia, parando ao encontrar obstáculo,
/// caixa ou o limite inferior do grid (o chão-morte). Processa de baixo pra cima pra que apoios
/// assentem antes do que está sobre eles. Não precisa saber quem se moveu nem quem perdeu o
/// apoio: quem já está apoiado simplesmente não desce.
///
/// É usada tanto pelo empurrão do player (<see cref="ECS.Systems.MovementSystem"/>) quanto
/// pelo bloco que some sob uma peça (<see cref="ECS.Systems.PressurePlateSystem"/>). A queda só
/// muda a célula da peça; o histórico de undo captura o deslocamento líquido do turno por conta
/// própria (snapshot antes/depois), então a gravidade não grava nada.
/// </summary>
public static class Gravity
{
    public static void Settle(GameWorld world)
    {
        // Toda peça móvel que ocupa o grid é candidata (player, caixas, inimigos). Obstáculos e
        // toggles não têm SpawnPosition — são terreno, não caem. Coleta antes de mover: o Move
        // muta o grid, o que não pode acontecer durante a iteração da query.
        var movers = new List<Entity>();
        var query = new QueryDescription().WithAll<GridPosition, SpawnPosition, Solid>();
        world.World.Query(in query, (Entity e) => movers.Add(e));

        // Assenta de baixo pra cima: o apoio pousa antes do que repousa sobre ele. Empate de Y
        // processa o player primeiro — se ele cair, o grude magnético já não existe quando a vez
        // da caixa chegar, e ela cai junto em vez de ficar pairando órfã.
        movers.Sort((a, b) =>
        {
            int ya = world.World.Get<GridPosition>(a).Y;
            int yb = world.World.Get<GridPosition>(b).Y;
            if (ya != yb)
                return ya.CompareTo(yb);
            return world.World.Has<Player>(b).CompareTo(world.World.Has<Player>(a));
        });

        foreach (var e in movers)
        {
            // Caixa magnética grudada no player não cai: o grude a segura, mesmo pairando
            // sobre um buraco. Checado na hora, já refletindo quedas anteriores.
            if (Magnetism.IsHeld(world, e))
                continue;

            var pos = world.World.Get<GridPosition>(e);
            int ny = pos.Y;
            while (world.FootprintFree(e, pos.X, ny - 1, pos.Z))
                ny--;

            if (ny != pos.Y)
                world.Move(e, new GridPosition(pos.X, ny, pos.Z));
        }
    }
}
