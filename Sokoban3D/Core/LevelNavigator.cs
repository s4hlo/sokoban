using System;
using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.ECS.Components;
using Sokoban3D.Levels;

namespace Sokoban3D.Core;

/// <summary>
/// Navegação na árvore de níveis: dona da pilha de sessões e do cache que as preserva.
/// Cada nível é uma sessão isolada (<see cref="GameWorld"/>) — não há hub especial, a raiz
/// é só mais um nível. O topo da pilha é a sessão ativa; entrar num portal empilha um filho;
/// concluir o objetivo descarta o topo. O nível-pai fica preservado intacto enquanto se está
/// num filho. Separa essa responsabilidade do loop do MonoGame (Game1), que só repassa eventos.
/// </summary>
public class LevelNavigator
{
    private readonly LevelManager _levelManager;
    private readonly LevelCatalog _catalog;

    // Pilha de navegação na árvore: o topo é a sessão ativa; cada portal empilha um filho.
    private readonly Stack<GameWorld> _stack = new();

    // Cache de sessões por id de nível. Cada nível visitado mantém sua sessão viva aqui,
    // então reentrar restaura o estado (caixas onde você deixou). A pilha guarda só o
    // caminho atual na árvore; a preservação vem deste cache.
    private readonly Dictionary<int, GameWorld> _sessions = new();

    /// <summary>Disparado sempre que a sessão ativa muda (entrar/concluir/suspender/raiz).</summary>
    public event Action LevelChanged;

    public LevelNavigator(LevelManager levelManager, LevelCatalog catalog)
    {
        _levelManager = levelManager;
        _catalog = catalog;
    }

    /// <summary>A sessão ativa (topo da pilha).</summary>
    public GameWorld Active => _stack.Peek();

    /// <summary>Começa na raiz da árvore de níveis (um nível como qualquer outro).</summary>
    public void EnterRoot()
    {
        _stack.Push(OpenLevel(LevelCatalog.RootId));
        LevelChanged?.Invoke();
    }

    /// <summary>
    /// No nível atual: se o player estiver sobre um portal, mergulha no nível dele. Retorna
    /// true se entrou (a sessão ativa mudou).
    /// </summary>
    public bool TryEnterPortal()
    {
        var playerPos = FindPlayerCell(Active);
        if (playerPos is null)
            return false;

        var p = playerPos.Value;
        var portalEntity = Active.Spatial.CellWith<LevelPortal>(p.X, p.Y, p.Z);
        if (portalEntity is null)
            return false;

        int target = Active.World.Get<LevelPortal>(portalEntity.Value).LevelIndex;
        _stack.Push(OpenLevel(target));
        LevelChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Conclui o nível ativo (chegou na meta): marca como concluído e volta ao pai, DESCARTANDO
    /// a sessão. Como sai do cache, a próxima visita reconstrói o nível do zero — estado e
    /// histórico limpos. Pra sair preservando o nível, use <see cref="SuspendActive"/>.
    /// </summary>
    public void CompleteActive()
    {
        // A raiz da árvore não tem pai pra onde voltar; guarda genérica (ela também não
        // tem objetivo, então na prática nunca chega aqui).
        if (_stack.Count <= 1)
            return;

        var done = _stack.Pop();
        _sessions.Remove(done.LevelId);
        done.Dispose();
        _catalog.MarkCompleted(done.LevelId);

        // Atualiza os portais do pai pra refletir a conclusão (o que acabou de ficar verde).
        RefreshPortals(Active);
        LevelChanged?.Invoke();
    }

    /// <summary>
    /// Suspende o nível ativo (tecla T): volta ao pai sem concluir, MANTENDO a sessão no cache.
    /// Reentrar restaura o nível exatamente onde parou (caixas e histórico). É o oposto de
    /// concluir pela meta, que reseta. Na raiz não faz nada (não há pai pra onde voltar).
    /// </summary>
    public void SuspendActive()
    {
        if (_stack.Count <= 1)
            return;

        _stack.Pop(); // permanece em _sessions, preservado
        RefreshPortals(Active);
        LevelChanged?.Invoke();
    }

    /// <summary>
    /// Abre o nível de id <paramref name="id"/>: devolve a sessão cacheada (estado preservado)
    /// ou cria uma nova a partir da receita na primeira visita. Sempre ressincroniza os portais
    /// com a conclusão global (um neto concluído noutro ramo deve aparecer verde aqui também).
    /// </summary>
    private GameWorld OpenLevel(int id)
    {
        if (!_sessions.TryGetValue(id, out var session))
        {
            var level = _catalog.BuildLevel(id);
            session = new GameWorld(level.Width, level.Height, level.Depth);
            _levelManager.LoadLevel(session, level);
            _sessions[id] = session;
        }

        RefreshPortals(session);
        return session;
    }

    /// <summary>Sincroniza o estado "concluído" dos portais de uma sessão com o registro global.</summary>
    private void RefreshPortals(GameWorld session)
    {
        var query = new QueryDescription().WithAll<LevelPortal>();
        session.World.Query(in query, (ref LevelPortal portal) =>
        {
            portal.Completed = _catalog.IsCompleted(portal.LevelIndex);
        });
    }

    private static GridPosition? FindPlayerCell(GameWorld session)
    {
        var player = session.Spatial.First<Player>();
        return player is null ? null : session.World.Get<GridPosition>(player.Value);
    }

    public void Dispose()
    {
        // O cache é dono de todas as sessões (a pilha só referencia um subconjunto).
        foreach (var session in _sessions.Values)
            session.Dispose();
        _sessions.Clear();
        _stack.Clear();
    }
}
