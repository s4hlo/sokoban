namespace Sokoban3D.ECS.Components;

/// <summary>
/// Marca uma entity como player
/// </summary>
public struct Player
{
    public float Speed;
}

/// <summary>
/// Marca uma entity como caixa
/// </summary>
public struct Box
{
    public int Weight; // 1 = normal, 2 = pesada, etc
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
