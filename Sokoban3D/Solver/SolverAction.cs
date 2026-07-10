namespace Sokoban3D.Solver;

/// <summary>
/// As ações que o solver considera. As quatro direções seguem a mesma convenção do teclado
/// (<c>W=(0,-1) S=(0,1) A=(-1,0) D=(1,0)</c>, ver <c>MovementSystem.GetFreshDirection</c>).
/// <see cref="Undo"/> só entra no conjunto quando o nível tem elemento atemporal
/// (<c>TimelessBase</c>) ou caixa verde (<c>Permanent</c>) — fora disso o undo é inverso
/// perfeito e nunca alcança estado novo (ver <c>PuzzleSolver</c>).
/// </summary>
public enum SolverAction
{
    Up,    // W: (0, -1)
    Down,  // S: (0, +1)
    Left,  // A: (-1, 0)
    Right, // D: (+1, 0)
    Undo,  // Z
}

/// <summary>Utilitários das ações: mapeamento ação → delta no plano X/Z.</summary>
public static class SolverActions
{
    /// <summary>As quatro direções de movimento (sem o Undo).</summary>
    public static readonly SolverAction[] Directions =
    {
        SolverAction.Up, SolverAction.Down, SolverAction.Left, SolverAction.Right,
    };

    /// <summary>O delta (dx,dz) da ação; (0,0) pra <see cref="SolverAction.Undo"/>.</summary>
    public static (int Dx, int Dz) Delta(SolverAction action) => action switch
    {
        SolverAction.Up => (0, -1),
        SolverAction.Down => (0, 1),
        SolverAction.Left => (-1, 0),
        SolverAction.Right => (1, 0),
        _ => (0, 0),
    };
}
