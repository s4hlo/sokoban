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
/// Marca uma entity como caixa, com seu tipo. Uma caixa quebrada (frágil destruída) perde
/// o tag <see cref="Solid"/> — deixa de ocupar o grid e de ser desenhada —, mas a entity
/// persiste pra que o undo consiga reverter a quebra (readicionando o <see cref="Solid"/>).
/// </summary>
public struct Box
{
    public BoxType Type;
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
/// Tag: a entity ocupa uma célula do grid (player, caixa não-quebrada, inimigo, obstáculo).
/// É a ÚNICA fonte de verdade sobre ocupação no nível do ECS — "essa peça ocupa o grid?"
/// é sempre <c>World.Has&lt;Solid&gt;(e)</c>. Quebrar uma frágil remove o tag; o undo o
/// readiciona. Objetivos e portais NÃO têm Solid (o player precisa pisar em cima).
/// </summary>
public struct Solid
{
}

/// <summary>
/// Marca uma entity como objetivo: a célula-meta que o player precisa alcançar
/// para concluir o nível. Não ocupa o grid (o player pode pisar nela).
/// </summary>
public struct Objective
{
    public int Id;
}

/// <summary>
/// Marca uma célula como entrada (portal) para um nível filho. O player anda até ela e
/// pressiona Enter para mergulhar no nível indicado. Qualquer nível pode ter portais — é
/// assim que a árvore se ramifica. Não ocupa o grid.
/// Completed = nível já concluído (muda a cor do portal).
/// </summary>
public struct LevelPortal
{
    public int LevelIndex;
    public bool Completed;
}

/// <summary>
/// Marca uma entity como obstáculo: um bloco sólido fixo (voxel 1×1×1) que forma o
/// terreno. Não é empurrável e ocupa o grid — bloqueia o movimento no seu nível (não
/// dá pra subir nele) e sustenta o que estiver na célula de cima. Empilhados, criam
/// paredes e plataformas; onde faltam, a peça cai.
/// </summary>
public struct Obstacle
{
}

/// <summary>
/// Marca uma entity como inimigo
/// </summary>
public struct Enemy
{
    public float Speed;
}

/// <summary>
/// Célula inicial (spawn) da peça no grid. Guardada na própria entity pra que o
/// restart (R) consiga reposicioná-la sem destruir/recriar — mantendo o mesmo
/// handle de Entity válido, o que permite o undo reverter o próprio restart.
/// </summary>
public struct SpawnPosition
{
    public int X;
    public int Y;
    public int Z;

    public SpawnPosition(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}
