using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Core;

/// <summary>
/// Regra da carga sobre a caixa frágil (<see cref="BoxType.Fragile"/>): subir em cima dela não
/// a quebra, mas a peça que estava repousando em cima SAIR quebra — mesmo que outra entre no
/// lugar. É a armadilha de pressão: o peso arma, remover o peso dispara.
///
/// Nada é armazenado em componente: a carga é capturada do grid no início de cada turno
/// (<see cref="CaptureLoads"/>) e conferida depois do turno assentado
/// (<see cref="ResolveDepartures"/>). O undo só restaura posições/solidez e a próxima captura
/// re-deriva tudo, no mesmo espírito de <see cref="Magnetism"/> e <see cref="Stickiness"/>.
/// </summary>
public static class Fragility
{
    /// <summary>
    /// As frágeis "armadas" agora: cada frágil sólida com a peça móvel (Solid com
    /// <see cref="SpawnPosition"/> — player, caixa, inimigo) repousando na célula logo acima.
    /// Terreno (obstáculo, toggle) não conta como carga: nunca "sai". Chamar no início do
    /// turno, antes de qualquer mutação.
    /// </summary>
    public static List<(Entity Box, Entity Loader)> CaptureLoads(GameWorld world)
    {
        var loads = new List<(Entity Box, Entity Loader)>();
        var query = new QueryDescription().WithAll<Box, GridPosition, Solid>();
        world.World.Query(in query, (Entity e, ref Box b, ref GridPosition p) =>
        {
            if (b.Type != BoxType.Fragile)
                return;
            if (world.Grid.Occupant(p.X, p.Y + 1, p.Z) is { } loader
                && world.World.Has<SpawnPosition>(loader))
                loads.Add((e, loader));
        });
        return loads;
    }

    /// <summary>
    /// Depois do turno assentado (movimento + gravidade + placas): toda frágil armada cuja
    /// carga não está mais na célula logo acima quebra — não importa se a célula ficou vazia
    /// ou se outra peça entrou. A quebra derruba o que perdeu apoio (re-assenta) e pode
    /// desarmar/disparar outras frágeis em cadeia, então itera até estabilizar. Devolve true
    /// se alguma quebrou (o chamador re-deriva as placas). Chamar ANTES do commit do
    /// histórico, pra perda de Solid e quedas entrarem no mesmo turno.
    /// </summary>
    public static bool ResolveDepartures(GameWorld world, List<(Entity Box, Entity Loader)> loads)
    {
        bool any = false;
        bool broke = true;
        while (broke)
        {
            broke = false;
            foreach (var (box, loader) in loads)
            {
                // Já quebrou neste turno (prensada num empurrão, ou na iteração anterior).
                if (!world.World.Has<Solid>(box))
                    continue;

                var p = world.World.Get<GridPosition>(box);
                if (world.Grid.Occupant(p.X, p.Y + 1, p.Z) is { } occ && occ == loader)
                    continue; // a carga segue em cima: armada, mas intacta

                world.Vacate(box);
                world.World.Remove<Solid>(box);
                broke = true;
                any = true;
            }

            // Quebrou algo: quem repousava nas quebradas cai, o que pode tirar (ou pôr) carga
            // de outra frágil armada — a próxima volta confere.
            if (broke)
                Gravity.Settle(world);
        }
        return any;
    }
}
