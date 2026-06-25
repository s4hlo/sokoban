namespace Sokoban3D.ECS.Components;

/// <summary>
/// Marca uma entity como player
/// </summary>
public struct Player
{
    public float Speed;
}

/// <summary>
/// Tipos de caixa. O peso determina quanto "esforço" o player gasta pra empurrar.
/// </summary>
public enum BoxType
{
    Light,   // leve: sem peso, carregada de graça (até por outra caixa)
    Medium,  // média: o player empurra até duas em fila
    Heavy,   // pesada: só uma por vez
    Fragile, // frágil: empurra como leve, mas quebra se for contra algo que não move
    Permanent, // verde: empurra como pesada, mas o undo não a reverte (só o R volta ela)
}

/// <summary>
/// Marca uma entity como caixa, com seu tipo.
/// Broken = caixa quebrada (frágil destruída): não ocupa grid nem é desenhada,
/// mas a entity persiste pra que o undo consiga reverter a quebra.
/// </summary>
public struct Box
{
    public BoxType Type;
    public bool Broken;
}

/// <summary>
/// Regras de peso por tipo de caixa. Peso é o custo que o player gasta pra empurrar;
/// a soma dos pesos numa fila não pode passar da força do player.
/// </summary>
public static class BoxRules
{
    public static int Weight(BoxType type) => type switch
    {
        BoxType.Light => 0,
        BoxType.Medium => 1,
        BoxType.Heavy => 2,
        BoxType.Fragile => 0,
        BoxType.Permanent => 2, // mesmo peso da pesada
        _ => 1,
    };

    /// <summary>
    /// True se a caixa não deve ser registrada no histórico de undo: ela se move
    /// normalmente, mas o undo não a reverte — só o restart (R) volta à posição inicial.
    /// </summary>
    public static bool IgnoresUndo(BoxType type) => type == BoxType.Permanent;
}

/// <summary>
/// Marca uma entity como objetivo (onde caixa deve ir)
/// </summary>
public struct Objective
{
    public int Id;
}

/// <summary>
/// Marca uma entity como inimigo
/// </summary>
public struct Enemy
{
    public float Speed;
}
