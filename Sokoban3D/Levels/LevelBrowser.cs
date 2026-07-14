using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban3D.Levels;

/// <summary>
/// Estado da lista de níveis do MODO DE JOGO (tecla M): navegar e pular pra um nível, sem editar.
/// É o irmão simples da lista do editor (que também renomeia/reordena) — aqui só se navega e
/// escolhe. Lógica pura, sem MonoGame: o <see cref="Sokoban3D.Game1"/> faz a detecção de tecla e a
/// troca real (via <see cref="Sokoban3D.Core.LevelNavigator"/>); a apresentação fica no
/// <see cref="LevelListRenderer"/>. Enquanto <see cref="Visible"/>, é um modal — o jogo suspende o
/// input do player.
/// </summary>
public sealed class LevelBrowser
{
    private readonly List<(int Id, string Name)> _items = new();

    /// <summary>True enquanto a lista está aberta (modal): o input de jogo fica suspenso.</summary>
    public bool Visible { get; private set; }

    /// <summary>Níveis (id+nome) na ordem dos ids — a mesma ordem que os atalhos , . percorrem.</summary>
    public IReadOnlyList<(int Id, string Name)> Items => _items;

    /// <summary>Índice da linha selecionada (0-based, sempre dentro dos limites da lista).</summary>
    public int Selection { get; private set; }

    /// <summary>
    /// Abre a lista, remontando os itens do repositório (ordenados por id) e ancorando a seleção
    /// no nível atual, pra a navegação começar de onde o jogador está.
    /// </summary>
    public void Open(LevelRepository repo, int currentId)
    {
        _items.Clear();
        foreach (var id in repo.ListIds().OrderBy(i => i))
            _items.Add((id, NameOf(repo, id)));

        int at = _items.FindIndex(it => it.Id == currentId);
        Selection = at >= 0 ? at : 0;
        Visible = true;
    }

    public void Close() => Visible = false;

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

    /// <summary>Nome de um nível lido do disco (o "?" cobre um arquivo ilegível, como no editor).</summary>
    private static string NameOf(LevelRepository repo, int id)
    {
        try { return repo.Load(id).Name; }
        catch { return "?"; }
    }
}
