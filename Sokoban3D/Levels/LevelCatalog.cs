using System.Collections.Generic;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Levels;

/// <summary>
/// Catálogo de níveis como uma árvore. NÃO existe "hub" especial: todo nível é a mesma
/// abstração (<see cref="Level"/>) — pode ter portais (filhos na árvore) e/ou um objetivo
/// (meta que conclui e devolve ao pai). O que chamamos de "hub" é só o nível-raiz, que por
/// acaso tem vários portais e nenhum objetivo; ele é montado pelo mesmo <see cref="BuildLevel"/>
/// que qualquer outro. A árvore pode se ramificar tão fundo quanto se quiser — basta um nível
/// apontar portais pra outros ids. A navegação (pilha de sessões) vive no Game1; aqui só ficam
/// as receitas e o registro global de conclusão por id.
/// </summary>
public class LevelCatalog
{
    /// <summary>Nível onde o jogo começa (a raiz da árvore). Nada de especial além de ser a raiz.</summary>
    public const int RootId = 0;

    // Conclusão global por id: um nível concluído continua "verde" em qualquer pai que aponte pra ele.
    private readonly HashSet<int> _completed = new();

    public bool IsCompleted(int levelId) => _completed.Contains(levelId);

    public void MarkCompleted(int levelId) => _completed.Add(levelId);

    /// <summary>Monta o nível de id <paramref name="id"/> — inclusive a raiz, do mesmo jeito.</summary>
    public Level BuildLevel(int id) => id switch
    {
        0 => BuildRoot(),
        1 => BuildAquecimento(),
        2 => BuildDesvio(),
        3 => BuildEmpurra(),
        _ => BuildRoot(),
    };

    // Id 0 — raiz: 11x11, sem objetivo, portais pros níveis 1, 2 e 3. É um nível como outro
    // qualquer; só não tem meta, então sai-se dele descendo num portal (ou Esc pra fechar).
    private Level BuildRoot()
    {
        // Height >= 3 pra caber uma parede de 2 de altura (demo do flavor 3D). As peças
        // andam em y=1, sobre o piso de obstáculos em y=0.
        var level = new Level { Id = 0, Name = "Raiz", Width = 11, Height = 4, Depth = 11 };
        level.FillFloor();
        level.PlayerSpawns.Add((5, 1, 9));
        level.PortalSpawns.Add((3, 1, 3, 1, IsCompleted(1)));
        level.PortalSpawns.Add((5, 1, 3, 2, IsCompleted(2)));
        level.PortalSpawns.Add((7, 1, 3, 3, IsCompleted(3)));
        level.BoxSpawns.Add((2, 1, 6, BoxType.Heavy));
        level.BoxSpawns.Add((8, 1, 6, BoxType.Permanent));

        // --- Demo do flavor 3D (remova à vontade ao desenhar o nível pra valer) ---
        // Buraco no canto: ande até (1,1) pra cair no chão-morte e testar o congelamento.
        level.DigHole(1, 1);
        level.DigHole(1, 2);
        // Parede de 2 de altura: bloqueia o movimento e não dá pra subir.
        level.ObstacleSpawns.Add((5, 1, 6));
        level.ObstacleSpawns.Add((5, 2, 6));
        return level;
    }

    // Id 1 — "Aquecimento": caminho quase livre, prova o ciclo entrar→meta→voltar.
    private static Level BuildAquecimento()
    {
        var level = new Level { Id = 1, Name = "Aquecimento", Width = 7, Height = 4, Depth = 7 };
        level.FillFloor();
        level.PlayerSpawns.Add((3, 1, 5));
        level.ObjectiveSpawns.Add((3, 1, 1));
        level.BoxSpawns.Add((1, 1, 3, BoxType.Heavy));
        level.BoxSpawns.Add((5, 1, 3, BoxType.Light));
        level.BoxSpawns.Add((5, 1, 5, BoxType.Permanent));
        return level;
    }

    // Id 2 — "Desvio": parede de caixas com brecha, MAIS um portal interno pro nível 3.
    // É um nível que também ramifica: dá pra mergulhar no 3 e voltar, ou ir direto à meta.
    private Level BuildDesvio()
    {
        var level = new Level { Id = 2, Name = "Desvio", Width = 9, Height = 4, Depth = 9 };
        level.FillFloor();
        level.PlayerSpawns.Add((4, 1, 7));
        level.ObjectiveSpawns.Add((1, 1, 1));
        // Parede em z=4 com brecha em x=2.
        level.BoxSpawns.Add((3, 1, 4, BoxType.Medium));
        level.BoxSpawns.Add((4, 1, 4, BoxType.Medium));
        level.BoxSpawns.Add((5, 1, 4, BoxType.Medium));
        level.BoxSpawns.Add((6, 1, 4, BoxType.Heavy));
        level.BoxSpawns.Add((6, 1, 6, BoxType.Permanent));
        // Portal interno: este nível também ramifica para o nível 3.
        level.PortalSpawns.Add((7, 1, 7, 3, IsCompleted(3)));
        return level;
    }

    // Id 3 — "Empurra": caixa no caminho que pode ser empurrada ou contornada.
    private static Level BuildEmpurra()
    {
        var level = new Level { Id = 3, Name = "Empurra", Width = 9, Height = 4, Depth = 9 };
        level.FillFloor();
        level.PlayerSpawns.Add((4, 1, 7));
        level.ObjectiveSpawns.Add((6, 1, 1));
        level.BoxSpawns.Add((4, 1, 4, BoxType.Medium));
        level.BoxSpawns.Add((2, 1, 5, BoxType.Light));
        level.BoxSpawns.Add((6, 1, 4, BoxType.Heavy));
        level.BoxSpawns.Add((6, 1, 3, BoxType.Fragile));
        level.BoxSpawns.Add((2, 1, 3, BoxType.Permanent));
        return level;
    }
}
