namespace Sokoban3D.Editor;

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
    Eraser,
}
