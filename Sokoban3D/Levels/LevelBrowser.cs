using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban3D.Levels;

/// <summary>
/// Estado do menu de listagem de níveis (tecla M) — o ÚNICO menu, aberto tanto jogando quanto no
/// editor. Guarda os itens (id+nome+badges) e a seleção; navegar, reordenar e escolher são
/// dirigidos pelo <see cref="Sokoban3D.Game1"/> (que tem o repositório e o
/// <see cref="Sokoban3D.Core.LevelNavigator"/>). Lógica pura, sem MonoGame; a apresentação fica no
/// <see cref="LevelListRenderer"/>. Enquanto <see cref="Visible"/>, é um modal — suspende o resto
/// do input.
/// </summary>
public sealed class LevelBrowser
{
    private readonly List<(int Id, string Name, LevelBadges Badges)> _items = new();

    /// <summary>True enquanto o menu está aberto (modal): o resto do input fica suspenso.</summary>
    public bool Visible { get; private set; }

    /// <summary>Níveis (id+nome+badges) na ordem dos ids — a mesma ordem que os atalhos , . percorrem.</summary>
    public IReadOnlyList<(int Id, string Name, LevelBadges Badges)> Items => _items;

    /// <summary>Índice da linha selecionada (0-based, sempre dentro dos limites da lista).</summary>
    public int Selection { get; private set; }

    /// <summary>
    /// Abre o menu, remontando os itens do repositório (ordenados por id) e ancorando a seleção
    /// no nível atual, pra a navegação começar de onde você está.
    /// </summary>
    public void Open(LevelRepository repo, int currentId)
    {
        Build(repo);
        int at = _items.FindIndex(it => it.Id == currentId);
        Selection = at >= 0 ? at : 0;
        Visible = true;
    }

    /// <summary>Remonta os itens do disco mantendo a seleção (usado após um reorder).</summary>
    public void Refresh(LevelRepository repo)
    {
        Build(repo);
        Selection = Math.Clamp(Selection, 0, Math.Max(0, _items.Count - 1));
    }

    public void Close() => Visible = false;

    /// <summary>Seleciona uma linha por índice (mouse/hover ou item movido no reorder); ignora fora dos limites.</summary>
    public void Select(int index)
    {
        if (index >= 0 && index < _items.Count)
            Selection = index;
    }

    public void MoveUp()
    {
        if (_items.Count > 0)
            Selection = Math.Max(0, Selection - 1);
    }

    public void MoveDown()
    {
        if (_items.Count > 0)
            Selection = Math.Min(_items.Count - 1, Selection + 1);
    }

    /// <summary>Id do nível selecionado, ou null se a lista está vazia.</summary>
    public int? SelectedId => _items.Count == 0 ? null : _items[Selection].Id;

    private void Build(LevelRepository repo)
    {
        _items.Clear();
        foreach (var id in repo.ListIds().OrderBy(i => i))
        {
            var (name, badges) = Describe(repo, id);
            _items.Add((id, name, badges));
        }
    }

    /// <summary>Nome + badges de um nível lido do disco (o "?" cobre um arquivo ilegível).</summary>
    private static (string Name, LevelBadges Badges) Describe(LevelRepository repo, int id)
    {
        try
        {
            var level = repo.Load(id);
            return (level.Name, LevelBadges.From(level));
        }
        catch { return ("?", default); }
    }
}
