using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Sokoban3D.ECS.Components;
using Sokoban3D.ECS.Systems;

namespace Sokoban3D.Levels;

/// <summary>
/// Quais mecânicas "de interesse" um nível tem — presença, não quantidade —, pra sinalizar na
/// listagem com um ícone (letra colorida) por tipo. Derivado da receita; as cores espelham as das
/// peças no jogo (ver <see cref="Sokoban3D.ECS.Systems.RenderSystem"/>), pra o badge bater com o
/// que aparece na cena. "Permanente" é o mecanismo atemporal (caixa verde ou base atemporal), não
/// a placa de pressão — placa anda junto com toggle, então seria redundante.
/// </summary>
public readonly struct LevelBadges
{
    public readonly bool Portal;
    public readonly bool Toggle;
    public readonly bool Magnetic;
    public readonly bool Permanent;

    public LevelBadges(bool portal, bool toggle, bool magnetic, bool permanent)
    {
        Portal = portal;
        Toggle = toggle;
        Magnetic = magnetic;
        Permanent = permanent;
    }

    public static LevelBadges From(Level level) => new(
        portal: level.PortalBoxSpawns.Count > 0,
        toggle: level.ToggleSpawns.Count > 0,
        magnetic: level.BoxSpawns.Exists(b => b.Type == BoxType.Magnetic),
        permanent: level.TimelessBaseSpawns.Count > 0
                   || level.BoxSpawns.Exists(b => b.Type == BoxType.Permanent));

    /// <summary>
    /// As cores dos quadradinhos das mecânicas presentes, em ordem fixa (portal, toggle,
    /// magnética, permanente). Reusa as cores das peças do <see cref="RenderSystem"/> — fonte
    /// única —, então o swatch bate com o que aparece na cena. Vazio se o nível não tem nenhuma.
    /// </summary>
    public IEnumerable<Color> Colors()
    {
        if (Portal) yield return RenderSystem.ColorOf(BoxType.Portal);
        if (Toggle) yield return RenderSystem.ToggleSolidColor;
        if (Magnetic) yield return RenderSystem.ColorOf(BoxType.Magnetic);
        if (Permanent) yield return RenderSystem.ColorOf(BoxType.Permanent);
    }
}
