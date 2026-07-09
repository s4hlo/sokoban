using Sokoban3D.ECS.Components;

namespace Sokoban3D.Editor;

/// <summary>
/// Um item clicável da paleta do HUD: a brush e, no caso das caixas, o tipo específico —
/// cada tipo de caixa tem a própria linha na paleta, em vez de ciclar num item só.
/// </summary>
public readonly record struct PaletteItem(EditorBrush Brush, BoxType? Box = null);

/// <summary>
/// O que o editor coloca na célula do cursor. Sólidos (obstáculo, caixa, player) ocupam o
/// grid e só um cabe por célula; marcadores (meta, portal) não ocupam. A borracha apaga tudo.
/// </summary>
public enum EditorBrush
{
    Obstacle,
    Box,
    Objective,
    Portal,
    Player,
    Plate,
    Toggle,
    TimelessBase,
    PortalBox,
    BigBox,
    Eraser,
}
