using System.Collections.Generic;
using System.IO;

namespace Sokoban3D.Levels;

/// <summary>
/// Catálogo de níveis como uma árvore. NÃO existe "hub" especial: todo nível é a mesma
/// abstração (<see cref="Level"/>) — pode ter portais (filhos na árvore) e/ou um objetivo
/// (meta que conclui e devolve ao pai). O que chamamos de "hub" é só o nível-raiz (id 0), que
/// por acaso tem vários portais e nenhum objetivo. A árvore pode se ramificar tão fundo quanto
/// se quiser — basta um nível apontar portais pra outros ids.
///
/// As receitas vivem como JSON em disco (<c>Maps/level_&lt;id&gt;.json</c>), a fonte de verdade:
/// o catálogo só carrega de lá. Aqui ficam também o registro global de conclusão por id.
/// </summary>
public class LevelCatalog
{
    /// <summary>Nível onde o jogo começa (a raiz da árvore). Nada de especial além de ser a raiz.</summary>
    public const int RootId = 0;

    // Os mapas vivem como JSON em disco; este repositório é a fonte de verdade.
    private readonly LevelRepository _repo;

    // Conclusão global por id: um nível concluído continua "verde" em qualquer pai que aponte pra ele.
    private readonly HashSet<int> _completed = new();

    public LevelCatalog(LevelRepository repo)
    {
        _repo = repo;
    }

    public bool IsCompleted(int levelId) => _completed.Contains(levelId);

    public void MarkCompleted(int levelId) => _completed.Add(levelId);

    /// <summary>
    /// Carrega a receita do nível <paramref name="id"/> do seu JSON em disco. Mapa ausente é
    /// erro (o JSON é a fonte de verdade) — falha com uma mensagem clara em vez de inventar um
    /// nível.
    /// </summary>
    public Level BuildLevel(int id)
    {
        if (!_repo.Exists(id))
            throw new FileNotFoundException($"Mapa do nível {id} não encontrado em {_repo.PathFor(id)}.");

        return _repo.Load(id);
    }
}
