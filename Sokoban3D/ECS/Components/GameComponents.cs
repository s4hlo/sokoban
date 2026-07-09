namespace Sokoban3D.ECS.Components;

/// <summary>
/// Marca uma entity como player
/// </summary>
public struct Player
{
    public float Speed;
}

/// <summary>
/// Direção pra onde o player está olhando, no plano X/Z (vetor cardinal unitário). Livre de
/// caixas magnéticas, só acompanha o último comando; com alguma grudada (ver
/// <see cref="Core.Magnetism"/>) é quem decide se um comando anda (alinhado) ou gira o corpo.
/// </summary>
public struct Facing
{
    public int Dx;
    public int Dz;

    /// <summary>
    /// O olhar como ângulo de yaw (radianos), na convenção do Matrix.CreateRotationY: o
    /// "frente" local (0,0,1) rotacionado por Yaw aponta pra (Dx, 0, Dz). Zero = Z+.
    /// </summary>
    public float Yaw => System.MathF.Atan2(Dx, Dz);

    /// <summary>Olhar inicial do spawn: Z+ (a direção da tecla S).</summary>
    public static Facing Default => new() { Dx = 0, Dz = 1 };
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
    Permanent, // verde: empurra como leve, mas o reverso não a desfaz (só o R volta ela)
    Portal,  // teleporta quem tenta entrar nela pro lado oposto do portal parceiro (mesmo Group)
    Magnetic, // gruda no player que encostar (ver Core.Magnetism): vira parte do corpo dele
}

/// <summary>
/// Marca uma entity como caixa, com seu tipo. Uma frágil quebrada perde o tag <see cref="Solid"/>,
/// mas a entity persiste pra o reverso conseguir re-solidificá-la.
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
        BoxType.Permanent => 0, // mesmo peso da leve (empurra de graça)
        BoxType.Portal => 0,    // empurrada de graça (só quando a saída está bloqueada)
        BoxType.Magnetic => 1,  // solta, empurra como média; grudada não é empurrável (é corpo)
        _ => 1,
    };
}

/// <summary>
/// Pareamento de uma caixa <see cref="BoxType.Portal"/>: duas com o mesmo <see cref="Group"/>
/// são um par. Quem tenta entrar numa delas é redirecionado pra o lado oposto da parceira (a
/// célula parceira + direção do movimento). Mesma convenção de <see cref="PressurePlate.Group"/>
/// e <see cref="Toggle.Group"/>. Um portal sem par é apenas uma caixa empurrável comum.
/// </summary>
public struct PortalBox
{
    public int Group;
}

/// <summary>
/// Tag: a entity ocupa uma célula do grid (player, caixa não-quebrada, inimigo, obstáculo).
/// É a ÚNICA fonte de verdade no ECS sobre ocupação — "ocupa o grid?" é <c>World.Has&lt;Solid&gt;(e)</c>.
/// Objetivos e portais NÃO têm Solid (o player precisa pisar em cima).
/// </summary>
public struct Solid
{
}

/// <summary>
/// Tag comum da família "marcador de célula": peças que ocupam uma célula lógica mas NÃO
/// ocupam o grid (o player/caixa pisa em cima) — objetivo, portal, placa de pressão, base
/// atemporal. Não substitui o componente específico de cada um (que carrega os dados e dá o
/// comportamento); é o conceito único pra consultar "tem um marcador aqui?" e pra essa família
/// crescer com novos tipos sem reinventar a categoria.
/// </summary>
public struct CellMarker
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
/// Base atemporal: marcador de célula (não ocupa o grid) que esvazia a pilha de quem termina um
/// passo sobre ela (<see cref="Core.History.Forget"/>) — a peça fica commitada ali. É um expurgo
/// único do passado, não um flag: sair e se mover volta a empilhar. Difere da caixa
/// <see cref="BoxType.Permanent"/> (verde), que ignora o reverso pra sempre — aqui o efeito é da célula.
/// </summary>
public struct TimelessBase
{
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
/// Placa de pressão: aciona enquanto houver uma peça que ocupa o grid (player ou caixa) em
/// cima dela. NÃO ocupa o grid (a peça precisa poder pisar). Liga-se aos blocos
/// <see cref="Toggle"/> de mesmo <see cref="Group"/>: pressionada, ela inverte o estado deles.
/// O acionamento é momentâneo — some o peso, o bloco volta ao default.
/// </summary>
public struct PressurePlate
{
    public int Group;
}

/// <summary>
/// Bloco controlável por <see cref="PressurePlate"/>: um voxel sólido que aparece/some
/// conforme as placas do seu <see cref="Group"/>. <see cref="SolidByDefault"/> é o estado em
/// repouso (nenhuma placa pressionada): true = porta presente que SOME ao pisar; false = ponte
/// ausente que APARECE ao pisar. Pressionada qualquer placa do grupo, o estado inverte.
/// Quando sólido, ganha o tag <see cref="Solid"/> e ocupa o grid (igual a um obstáculo); quando
/// aberto, perde o tag e some. Essa solidez é estado DERIVADO das placas e NUNCA entra no histórico:
/// o reverso devolve as posições e o <see cref="Systems.PressurePlateSystem"/> re-deriva os toggles.
///
/// <see cref="Threshold"/> é quantas placas do grupo precisam estar pressionadas ao mesmo tempo
/// pra o bloco acionar: 1 = qualquer placa (lógica OR); igual ao total de placas do grupo = só
/// com TODAS pisadas (lógica AND). O sistema trata 0 como 1 (default).
/// </summary>
public struct Toggle
{
    public int Group;
    public bool SolidByDefault;
    public int Threshold;
}

/// <summary>
/// Marca uma entity como inimigo
/// </summary>
public struct Enemy
{
    public float Speed;
}

/// <summary>
/// Célula inicial (spawn) da peça. Guardada na entity pra o restart (R) reposicioná-la sem
/// destruir/recriar — o handle de Entity segue válido, então o reverso desfaz o próprio restart.
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
