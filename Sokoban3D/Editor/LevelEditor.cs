using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Serilog;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;
using Sokoban3D.Levels;
using Sokoban3D.Solver;

namespace Sokoban3D.Editor;

/// <summary>
/// Veredito da checagem de solvabilidade do nível em edição (rodada pelo solver em segundo
/// plano). <see cref="Idle"/> = ainda não checou desde a última edição.
/// </summary>
public enum EditorValidation { Idle, Running, Solvable, Unsolvable, Unknown, NoObjective }

/// <summary>
/// Editor de níveis in-game. Edita um <see cref="Level"/> (a receita, fonte de verdade do
/// design) e a cada mudança re-materializa a sessão ativa via <see cref="LevelManager.LoadLevel"/>
/// — reaproveitando todo o ECS e o render do jogo. O cursor e o HUD ficam por conta do
/// <see cref="EditorRenderer"/>; aqui mora o estado e a lógica de edição.
///
/// O mouse é a via principal: o picking acerta o bloco visível sob o ponteiro
/// (<see cref="MousePicker.PickVoxel"/>) e o clique coloca na face apontada — esquerdo coloca
/// (arrasto pinta na mesma camada), direito apaga, meio copia a brush do que está na célula,
/// Ctrl+esquerdo move o conteúdo de uma célula, roda troca a camada Y. Alt+arrasto orbita a
/// câmera do editor e Alt+roda dá zoom (câmera própria, ver <see cref="EditorCamera"/>). Toda
/// mutação passa por snapshot pra Ctrl+Z/Ctrl+Y desfazer/refazer.
/// </summary>
public class LevelEditor
{
    private const int MinSize = 1;
    private const int MaxSize = 32;
    private const int MaxUndo = 100;

    private readonly LevelManager _levels;
    private readonly LevelRepository _repo;

    // Receita em edição (clone do nível ativo). Null fora do modo editor.
    private Level _working;
    private GameWorld _session;
    private KeyboardState _prev;
    private MouseState _prevMouse;

    // Câmera orbitável do editor (própria, não a fixa do jogo): o editor a controla e é a
    // fonte das matrizes de view/projection usadas tanto no render quanto no picking.
    private readonly EditorCamera _cam = new();
    // Reenquadrar na próxima Update (quando já se tem a viewport): setado ao entrar e a cada
    // mudança de dimensão do grid.
    private bool _reframeCam;

    // Viewport do frame, pro picking do mouse (atualizada a cada Update).
    private Viewport _viewport;
    private Matrix _view;
    private Matrix _projection;

    // Undo/redo de edição: snapshots da receita inteira (níveis são pequenos, clonar é barato).
    // Um arrasto de pintura conta como UMA edição: o snapshot é tirado no aperto do botão.
    private readonly List<Level> _undoStack = new();
    private readonly List<Level> _redoStack = new();

    // Arrasto de pintura/borracha: camada Y travada no aperto do botão, senão pintar um bloco
    // faria o raycast do frame seguinte acertar o topo dele e o arrasto subiria em escada.
    private int? _dragY;
    private bool _dragErase;

    // Mover (Ctrl+arrasto): conteúdo extraído da célula de origem, esperando o drop.
    private Level _grabbed;
    private (int X, int Y, int Z) _grabbedFrom;

    // Checagem de solvabilidade em segundo plano: a busca (que pode levar segundos) roda num
    // Task sobre um clone da receita, pra não travar o loop do jogo. O Update faz o polling.
    private Task<SolveResult> _validationTask;

    // Confirmação de duas etapas pras ações que descartam edições não salvas (novo/carregar):
    // a primeira arma, a repetição confirma. Qualquer edição ou salvamento limpa (ver ConfirmDestructive).
    private enum Confirm { None, New, Load }
    private Confirm _pendingConfirm = Confirm.None;

    // Renomear inline: enquanto ativo, o teclado alimenta o buffer (via OnTextInput, o evento de
    // texto do Game1) em vez de mover o cursor; Enter confirma, Esc cancela.
    private bool _renaming;
    private string _nameBuffer = "";

    // Lista de níveis pra reordenar (snapshot id+nome do repositório, montado ao abrir e a cada
    // troca). Modal como o rename: enquanto aberta, o teclado navega/reordena a lista.
    private readonly List<(int Id, string Name)> _levelList = new();
    private int _listSelection;
    // Pedido de pular pra edição de outro nível (Enter na lista), consumido pelo Game1 após a
    // Update. Não trocamos a sessão aqui dentro: quem faz é o navigator, senão o nível pulado
    // viraria a raiz da pilha e concluir a meta não voltaria pro pai.
    private int? _pendingJump;
    // O conjunto de ids em disco mudou nesta sessão de edição (reordenar ou duplicar)? O Game1
    // consome ao sair ou pular pra reconstruir a navegação — o cache do navigator é por id e vira
    // obsoleto. Não é resetado no Enter: precisa sobreviver ao Enter da lista (que reabre o editor).
    private bool _catalogChanged;

    // ----- Estado exposto pro renderer -----
    public int CursorX { get; private set; }
    public int CursorY { get; private set; }
    public int CursorZ { get; private set; }
    public EditorBrush Brush { get; private set; } = EditorBrush.Obstacle;
    public BoxType BoxType { get; private set; } = BoxType.Medium;
    // Tipo do próximo obstáculo a ser colocado (reselecionar a brush Obstáculo cicla).
    public ObstacleType ObstacleType { get; private set; } = ObstacleType.Normal;
    // Eixo da próxima caixa grande a ser colocada (ou da que já está sob o cursor, ao reselecionar).
    public BigBoxAxis BigBoxAxis { get; private set; } = BigBoxAxis.X;
    // Tipo do próximo trilho a ser colocado (reselecionar a brush Trilho cicla; [ ] também).
    public RailType RailType { get; private set; } = RailType.StraightX;
    public int PortalTarget { get; private set; } = 1;
    // Grupo que liga placas aos blocos toggle, e o estado de repouso do bloco que será colocado.
    public int Group { get; private set; } = 0;
    public bool ToggleSolidByDefault { get; private set; } = true;
    // Quantas placas do grupo precisam estar pisadas pra o bloco toggle acionar (1 = OR; total = AND).
    public int ToggleThreshold { get; private set; } = 1;
    public Level Working => _working;
    public string Status { get; private set; } = "";
    /// <summary>True quando o status é um aviso (algo não deu certo) — o HUD colore diferente.</summary>
    public bool StatusIsWarning { get; private set; }
    /// <summary>True enquanto um Ctrl+arrasto carrega o conteúdo de uma célula.</summary>
    public bool IsMoving => _grabbed != null;
    /// <summary>True enquanto Shift está pressionado (WASD/QE redimensionam o grid, não movem o cursor).</summary>
    public bool IsResizing { get; private set; }
    /// <summary>True quando a receita tem edições ainda não gravadas em disco (marcador no HUD).</summary>
    public bool IsDirty { get; private set; }
    /// <summary>Mostra a barra de atalhos completa (alterna com H) — desligada, só uma dica mínima.</summary>
    public bool ShowHelp { get; private set; } = true;
    /// <summary>Desenha a grade do plano de trabalho na camada Y ativa (alterna com G).</summary>
    public bool ShowPlane { get; private set; } = true;
    /// <summary>Último veredito de solvabilidade (tecla V). Zera pra Idle a cada edição.</summary>
    public EditorValidation Validation { get; private set; } = EditorValidation.Idle;
    /// <summary>True enquanto se digita o novo nome do nível (Ctrl+R). O HUD mostra o buffer.</summary>
    public bool IsRenaming => _renaming;
    /// <summary>Texto sendo digitado no rename (só válido com <see cref="IsRenaming"/>).</summary>
    public string NameBuffer => _nameBuffer;
    /// <summary>Lista de níveis pra reordenar (tecla L ou M) está aberta.</summary>
    public bool ShowLevelList { get; private set; }
    /// <summary>True em qualquer modo modal (rename ou lista): o Game1 segura Tab/Esc.</summary>
    public bool IsModal => _renaming || ShowLevelList;
    /// <summary>Níveis (id+nome) da lista de reordenação, na ordem dos ids.</summary>
    public IReadOnlyList<(int Id, string Name)> LevelList => _levelList;
    /// <summary>Índice do nível selecionado na lista de reordenação.</summary>
    public int ListSelection => _listSelection;

    // Matrizes da câmera do editor, consumidas pelo Game1 pra desenhar a cena enquanto edita.
    public Matrix View => _cam.View;
    public Matrix Projection => _cam.Projection;

    /// <summary>Disparado quando as dimensões do grid mudam (a câmera precisa reenquadrar).</summary>
    public event Action GridChanged;

    public LevelEditor(LevelManager levels, LevelRepository repo)
    {
        _levels = levels;
        _repo = repo;
    }

    /// <summary>
    /// Entra no editor sobre a sessão dada: clona a receita ativa e recarrega o nível pristino
    /// (o editor mostra o design, não o estado de jogo). Recebe o teclado atual pra a detecção
    /// de borda não disparar a brush no mesmo frame do Tab.
    /// </summary>
    public void Enter(GameWorld session, KeyboardState keyboard, MouseState mouse, Viewport viewport)
    {
        _session = session;
        _working = session.CurrentLevel?.Clone() ?? BlankLevel(-1);
        _prev = keyboard;
        _prevMouse = mouse;
        _viewport = viewport;

        _undoStack.Clear();
        _redoStack.Clear();
        _dragY = null;
        _grabbed = null;
        IsDirty = false;
        Validation = EditorValidation.Idle;
        _validationTask = null;
        _pendingConfirm = Confirm.None;
        _renaming = false;
        ShowLevelList = false;
        _pendingJump = null;

        CursorX = _working.Width / 2;
        CursorZ = _working.Depth / 2;
        CursorY = Math.Min(1, _working.Height - 1);
        SetStatus($"Editando \"{_working.Name}\"");

        // Enquadra a câmera do editor já aqui pra o primeiro frame (antes da primeira Update) não
        // desenhar com matriz zerada; resizes posteriores reenquadram via RaiseGridChanged.
        _cam.Frame(_working.Width, _working.Depth, viewport.AspectRatio);
        _reframeCam = false;
        Rebuild();
        GridChanged?.Invoke(); // reenquadra a câmera fixa do jogo (pra quando sair do editor)
    }

    /// <summary>
    /// Sai do editor: a receita editada vira o novo <see cref="GameWorld.CurrentLevel"/> e é
    /// recarregada, então o jogo recomeça já a partir do design editado.
    /// </summary>
    public void Exit(GameWorld session)
    {
        // Um move interrompido pelo Tab devolve o conteúdo pra origem (não pode evaporar).
        if (_grabbed != null)
        {
            InsertCell(_grabbed, _grabbedFrom, _grabbedFrom);
            _grabbed = null;
        }

        session.CurrentLevel = _working;
        _levels.LoadLevel(session, _working);
        _working = null;
        _session = null;
    }

    public void Update(GameWorld session, KeyboardState keyboard, MouseState mouse,
        Viewport viewport, PaletteItem? hudBrush, int? hudLevelRow = null)
    {
        _session = session;
        _viewport = viewport;

        // Reenquadra a câmera do editor quando pendente (agora que a viewport é conhecida), e
        // publica as matrizes correntes pro picking do mouse deste frame.
        if (_reframeCam)
        {
            _cam.Frame(_working.Width, _working.Depth, viewport.AspectRatio);
            _reframeCam = false;
        }
        _view = _cam.View;
        _projection = _cam.Projection;

        // Modos modais: enquanto renomeia ou navega a lista, o teclado vai só pra eles (o
        // texto do rename chega pelo evento OnTextInput). A cena segue desenhada por trás.
        if (_renaming)
        {
            HandleRename(keyboard);
            _prev = keyboard;
            _prevMouse = mouse;
            return;
        }
        if (ShowLevelList)
        {
            HandleLevelList(keyboard, mouse, hudLevelRow);
            _prev = keyboard;
            _prevMouse = mouse;
            return;
        }

        bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        bool ctrl = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        bool alt = keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);
        IsResizing = shift && !ctrl && !alt;

        PollValidation();
        HandleCameraKeys(keyboard);

        // Alt é o modo câmera (órbita/zoom): não move cursor, não redimensiona, não coloca.
        if (ctrl)
            HandleShortcuts(keyboard);
        else if (shift)
            HandleResize(keyboard);
        else if (!alt)
            HandleCursor(keyboard);

        HandleViewKeys(keyboard, ctrl);
        HandleBrushSelection(keyboard);
        if (!ctrl && !alt)
            HandlePlacement(keyboard);
        HandleMouse(mouse, hudBrush, ctrl, alt);

        _prev = keyboard;
        _prevMouse = mouse;
    }

    /// <summary>Zoom da câmera por teclado (+/−); a órbita e o zoom fino ficam no mouse (Alt).</summary>
    private void HandleCameraKeys(KeyboardState k)
    {
        if (Pressed(k, Keys.OemPlus)) _cam.Zoom(+1);
        if (Pressed(k, Keys.OemMinus)) _cam.Zoom(-1);
    }

    // ----- Teclas de visão / ferramentas (sem modificador) -----

    /// <summary>H alterna a ajuda, G a grade do plano, V dispara a checagem de solvabilidade.
    /// Ignoradas com Ctrl pra não colidir com os atalhos (Ctrl+... nunca é uma dessas).</summary>
    private void HandleViewKeys(KeyboardState k, bool ctrl)
    {
        if (ctrl)
            return;
        if (Pressed(k, Keys.H)) ShowHelp = !ShowHelp;
        if (Pressed(k, Keys.G)) ShowPlane = !ShowPlane;
        if (Pressed(k, Keys.V)) Validate();
        if (Pressed(k, Keys.L) || Pressed(k, Keys.M)) ToggleLevelList();
        if (Pressed(k, Keys.R)) BeginRename();
    }

    // ----- Atalhos com Ctrl -----

    private void HandleShortcuts(KeyboardState k)
    {
        if (Pressed(k, Keys.Z)) Undo();
        if (Pressed(k, Keys.Y)) Redo();
        if (Pressed(k, Keys.S)) SaveToDisk();
        if (Pressed(k, Keys.O)) LoadFromDisk();
        if (Pressed(k, Keys.N)) NewBlank();
        if (Pressed(k, Keys.D)) Duplicate();
    }

    // ----- Renomear (R) -----

    private void BeginRename()
    {
        _renaming = true;
        _nameBuffer = _working.Name ?? "";
        SetStatus("Renomeando: digite, Enter confirma, Esc cancela");
    }

    /// <summary>
    /// Recebe um caractere digitado (via o evento de texto do Game1). Só age em modo rename:
    /// backspace apaga, caracteres imprimíveis entram (até 40). Enter/Esc ficam por conta do
    /// <see cref="HandleRename"/> (teclado polado), pra não duplicar com o evento.
    /// </summary>
    public void OnTextInput(char c)
    {
        if (!_renaming)
            return;
        if (c == '\b')
        {
            if (_nameBuffer.Length > 0)
                _nameBuffer = _nameBuffer[..^1];
        }
        else if (c >= ' ' && c != (char)127 && _nameBuffer.Length < 40)
        {
            _nameBuffer += c;
        }
    }

    private void HandleRename(KeyboardState k)
    {
        if (Pressed(k, Keys.Enter)) CommitRename();
        else if (Pressed(k, Keys.Escape)) CancelRename();
    }

    private void CommitRename()
    {
        _renaming = false;
        var name = _nameBuffer.Trim();
        if (name.Length == 0)
        {
            SetStatus("Nome vazio: mantido o anterior", warning: true);
            return;
        }
        if (name == _working.Name)
        {
            SetStatus("Nome inalterado");
            return;
        }
        PushUndo();
        _working.Name = name;
        SetStatus($"Renomeado para \"{name}\"");
    }

    private void CancelRename()
    {
        _renaming = false;
        SetStatus("Renomear cancelado");
    }

    // ----- Lista de níveis / reordenar (L ou M) -----

    private void ToggleLevelList()
    {
        ShowLevelList = !ShowLevelList;
        if (ShowLevelList)
        {
            RefreshLevelList();
            SetStatus("Lista: W/S navega, Shift+W/S reordena, Enter edita, M/L/Esc fecha");
        }
    }

    /// <summary>Remonta a lista (id+nome) a partir do repositório, ordenada por id.</summary>
    private void RefreshLevelList()
    {
        _levelList.Clear();
        foreach (var id in _repo.ListIds().OrderBy(i => i))
            _levelList.Add((id, NameOf(id)));
        _listSelection = Math.Clamp(_listSelection, 0, Math.Max(0, _levelList.Count - 1));
    }

    /// <summary>Nome de um nível: o do working (edições não salvas) se for ele, senão lê do disco.</summary>
    private string NameOf(int id)
    {
        if (_working != null && _working.Id == id)
            return _working.Name;
        try { return _repo.Load(id).Name; }
        catch { return "?"; }
    }

    private void HandleLevelList(KeyboardState k, MouseState mouse, int? hoverRow)
    {
        // Mouse: passar por cima de uma linha a seleciona (como o cursor 3D segue o ponteiro);
        // clicar vai direto pro nível.
        if (hoverRow is int row)
        {
            bool mouseMoved = mouse.X != _prevMouse.X || mouse.Y != _prevMouse.Y;
            if (mouseMoved)
                _listSelection = row;
            if (LeftPressed(mouse))
            {
                _listSelection = row;
                JumpToSelected();
                return;
            }
        }

        if (Pressed(k, Keys.L) || Pressed(k, Keys.M) || Pressed(k, Keys.Escape))
        {
            ShowLevelList = false;
            return;
        }
        if (Pressed(k, Keys.Enter))
        {
            JumpToSelected();
            return;
        }
        if (_levelList.Count == 0)
            return;

        // W/S (ou setas, se houver) navegam; Shift+W/S reordenam. WASD é a via principal — o
        // teclado do usuário é 50%, sem cluster de setas.
        bool shift = k.IsKeyDown(Keys.LeftShift) || k.IsKeyDown(Keys.RightShift);
        bool up = Pressed(k, Keys.W) || Pressed(k, Keys.Up);
        bool down = Pressed(k, Keys.S) || Pressed(k, Keys.Down);
        if (up)
        {
            if (shift) MoveSelected(-1);
            else _listSelection = Math.Max(0, _listSelection - 1);
        }
        else if (down)
        {
            if (shift) MoveSelected(+1);
            else _listSelection = Math.Min(_levelList.Count - 1, _listSelection + 1);
        }
    }

    /// <summary>
    /// Fecha a lista e registra o pedido de pular pra edição do nível selecionado. A troca real
    /// (via navigator, pra a sessão ativa e a pilha ficarem corretas) é feita pelo Game1 em
    /// <see cref="TakePendingJump"/>, fora da Update do editor.
    /// </summary>
    private void JumpToSelected()
    {
        ShowLevelList = false;
        if (_levelList.Count == 0)
            return;

        int id = _levelList[_listSelection].Id;
        if (_working != null && id == _working.Id)
        {
            SetStatus($"Ja editando o nivel {id}");
            return;
        }

        _pendingJump = id;
        SetStatus($"Indo pro nivel {id}...");
    }

    /// <summary>
    /// Consome (e limpa) um pedido de pulo pendente. O Game1 chama após a Update: se houver id,
    /// devolve as edições à sessão atual (<see cref="Exit"/>), troca a sessão ativa pelo navigator
    /// e reabre o editor nela (<see cref="Enter"/>) — assim o nível pulado entra como sessão de
    /// verdade na árvore, e concluir a meta volta pro pai normalmente.
    /// </summary>
    public int? TakePendingJump()
    {
        var id = _pendingJump;
        _pendingJump = null;
        return id;
    }

    /// <summary>Move o selecionado em <paramref name="delta"/> na lista, trocando ids com o vizinho.</summary>
    private void MoveSelected(int delta)
    {
        int i = _listSelection, j = i + delta;
        if (j < 0 || j >= _levelList.Count)
            return;

        int idA = _levelList[i].Id, idB = _levelList[j].Id;
        SwapLevelIds(idA, idB);
        RefreshLevelList();
        _listSelection = j; // a seleção segue o item movido
        SetStatus($"Niveis {idA} <-> {idB} reordenados");
    }

    /// <summary>
    /// Troca as identidades (ids/arquivos) de dois níveis — reordena a lista. O conteúdo troca de
    /// arquivo; os portais NÃO são remapeados (mantêm o número que apontavam). Se um dos dois é o
    /// nível em edição, usa a receita em memória (preservando edições não salvas) e ela passa a
    /// ficar gravada com o novo id. Não entra no undo (é uma operação entre arquivos, não do design).
    /// </summary>
    private void SwapLevelIds(int idA, int idB)
    {
        var a = SwapSource(idA);
        var b = SwapSource(idB);
        a.Id = idB;
        b.Id = idA;
        _repo.Save(a);
        _repo.Save(b);

        // O working participou do swap: já foi gravado com o novo id, então não está mais "sujo".
        if (_working != null && (_working.Id == idA || _working.Id == idB))
            IsDirty = false;

        // O catálogo em disco mudou de forma: o Game1 vai reconstruir a navegação ao sair/pular.
        _catalogChanged = true;
        Log.Information("Editor: niveis {A} e {B} reordenados (ids trocados)", idA, idB);
    }

    /// <summary>
    /// Consome (e limpa) o sinal de que o conjunto de ids em disco mudou (reordenar/duplicar). O
    /// Game1 chama nos handoffs editor→jogo pra reconstruir a navegação
    /// (<see cref="LevelNavigator.Reload"/>), senão o cache por id do navigator fica obsoleto.
    /// </summary>
    public bool ConsumeCatalogChanged()
    {
        var v = _catalogChanged;
        _catalogChanged = false;
        return v;
    }

    /// <summary>Conteúdo-fonte de um nível no swap: o working em memória se for ele, senão do disco.</summary>
    private Level SwapSource(int id)
        => _working != null && _working.Id == id ? _working : _repo.Load(id);

    // ----- Undo / redo -----

    /// <summary>
    /// Tira um snapshot da receita ANTES de uma mutação, pra o Ctrl+Z voltar a ela. É o ponto
    /// único por onde toda edição passa, então também marca a receita como "não salva" e invalida
    /// o último veredito de solvabilidade (que passa a ser sobre um design que já mudou).
    /// </summary>
    private void PushUndo()
    {
        _undoStack.Add(_working.Clone());
        if (_undoStack.Count > MaxUndo)
            _undoStack.RemoveAt(0);
        _redoStack.Clear();
        MarkChanged();
    }

    /// <summary>Marca a receita como divergente do disco e descarta o veredito de solvabilidade.</summary>
    private void MarkChanged()
    {
        IsDirty = true;
        _pendingConfirm = Confirm.None; // uma edição invalida qualquer "repita pra confirmar" pendente
        if (Validation != EditorValidation.Running)
            Validation = EditorValidation.Idle;
    }

    /// <summary>
    /// Portão de segurança das ações que jogam fora edições não salvas (novo/carregar): sem nada
    /// pendente libera direto; com alterações, arma um pedido e só libera se a MESMA ação for
    /// repetida antes de qualquer edição ou salvamento (que limpam o pedido). True = pode seguir.
    /// </summary>
    private bool ConfirmDestructive(Confirm action, string warning)
    {
        if (!IsDirty || _pendingConfirm == action)
        {
            _pendingConfirm = Confirm.None;
            return true;
        }
        _pendingConfirm = action;
        SetStatus($"{warning} — repita pra confirmar", warning: true);
        return false;
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
        {
            SetStatus("Nada para desfazer", warning: true);
            return;
        }
        _redoStack.Add(_working);
        _working = PopLast(_undoStack);
        AfterHistoryJump("Desfeito");
    }

    private void Redo()
    {
        if (_redoStack.Count == 0)
        {
            SetStatus("Nada para refazer", warning: true);
            return;
        }
        _undoStack.Add(_working);
        _working = PopLast(_redoStack);
        AfterHistoryJump("Refeito");
    }

    private void AfterHistoryJump(string what)
    {
        // Um move em andamento fica órfão: o snapshot restaurado já contém o item na origem.
        _grabbed = null;
        _dragY = null;
        MarkChanged(); // o design mudou de novo — veredito de solvabilidade fica obsoleto
        ClampCursor();
        Rebuild();
        RaiseGridChanged(); // as dimensões podem ter mudado junto
        SetStatus(what);
    }

    private static Level PopLast(List<Level> stack)
    {
        var level = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return level;
    }

    // ----- Mouse -----

    /// <summary>
    /// Toda a interação de mouse. O cursor segue o ponteiro (hover); esquerdo coloca a brush na
    /// célula da face apontada e arrastando pinta (camada travada na do primeiro bloco); direito
    /// apaga o que está sob o ponteiro; meio copia a brush (conta-gotas); Ctrl+esquerdo pega o
    /// conteúdo da célula e solta onde o botão for liberado.
    /// </summary>
    private void HandleMouse(MouseState mouse, PaletteItem? hudBrush, bool ctrl, bool alt)
    {
        HandleWheel(mouse, ctrl, alt);

        bool mouseMoved = mouse.X != _prevMouse.X || mouse.Y != _prevMouse.Y;

        // Modo câmera: Alt+arrasto (botão esquerdo) orbita. Não toca no grid — sai antes de
        // qualquer interação de brush/hover.
        if (alt)
        {
            if (mouse.LeftButton == ButtonState.Pressed && mouseMoved)
                _cam.Orbit(mouse.X - _prevMouse.X, mouse.Y - _prevMouse.Y);
            return;
        }

        // Um gesto em andamento (move/arrasto) tem prioridade sobre o HUD: soltar o botão em
        // cima de um rótulo ainda encerra o gesto em vez de deixá-lo pendurado.

        // Move em andamento: o cursor segue o ponteiro; soltar o botão larga o conteúdo.
        if (_grabbed != null)
        {
            if (mouseMoved && PickVoxel(mouse) is { } follow)
                MoveCursorTo(follow.Place ?? follow.Hit);
            if (mouse.LeftButton == ButtonState.Released)
                DropGrabbed();
            return;
        }

        // Arrasto de pintura/borracha em andamento: picking travado na camada do início.
        if (_dragY is int dragY)
        {
            bool held = _dragErase
                ? mouse.RightButton == ButtonState.Pressed
                : mouse.LeftButton == ButtonState.Pressed;
            if (!held)
            {
                _dragY = null;
            }
            else if (mouseMoved
                && MousePicker.PickCell(_viewport, _view, _projection, mouse.X, mouse.Y, _session.Grid, dragY) is { } cell
                && (cell.X != CursorX || dragY != CursorY || cell.Z != CursorZ))
            {
                MoveCursorTo((cell.X, dragY, cell.Z));
                if (_dragErase)
                    EraseCell(CursorX, CursorY, CursorZ);
                else
                    Apply();
                Rebuild();
            }
            return;
        }

        // Clique sobre um botão de brush do HUD troca a brush e consome o clique (não toca no grid).
        if (hudBrush is { } btn)
        {
            if (LeftPressed(mouse))
                SelectBrush(btn.Brush, btn.Box, btn.Obstacle);
            return;
        }

        var pick = PickVoxel(mouse);

        // Hover: o cursor segue o ponteiro (o teclado segue valendo com o mouse parado).
        if (mouseMoved && pick is { } hover)
            MoveCursorTo(HoverCell(hover));

        if (pick is not { } target)
            return;

        if (MiddlePressed(mouse))
        {
            EyedropAt(target);
        }
        else if (LeftPressed(mouse))
        {
            if (ctrl)
            {
                TryGrab(target);
                return;
            }

            // A borracha mira a célula com conteúdo (como o botão direito); as demais brushes
            // miram a célula vazia encostada na face apontada.
            (int X, int Y, int Z)? cell = Brush == EditorBrush.Eraser ? ContentCell(target) : target.Place;
            if (cell is not { } c)
            {
                SetStatus("Sem espaco na frente dessa face", warning: true);
                return;
            }
            PushUndo();
            MoveCursorTo(c);
            Apply();
            Rebuild();
            _dragY = CursorY;
            _dragErase = false;
        }
        else if (RightPressed(mouse))
        {
            var cell = ContentCell(target);
            PushUndo();
            MoveCursorTo(cell);
            EraseCell(cell.X, cell.Y, cell.Z);
            Rebuild();
            _dragY = cell.Y;
            _dragErase = true;
        }
    }

    /// <summary>Roda do mouse: Alt aproxima/afasta (zoom); Ctrl ajusta o valor ([ ]); solta, troca a camada Y.</summary>
    private void HandleWheel(MouseState mouse, bool ctrl, bool alt)
    {
        int ticks = (mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue) / 120;
        if (ticks == 0)
            return;

        if (alt)
        {
            _cam.Zoom(ticks);
        }
        else if (ctrl)
        {
            AdjustAtCursor(ticks);
        }
        else
        {
            CursorY += ticks;
            ClampCursor();
            SetStatus($"Camada Y = {CursorY}");
        }
    }

    private VoxelPick? PickVoxel(MouseState mouse)
        => MousePicker.PickVoxel(_viewport, _view, _projection, mouse.X, mouse.Y, _session.Grid, CursorY);

    /// <summary>
    /// Célula "com conteúdo" de um pick: a célula de colocação se algo mora nela (marcadores e
    /// toggles não-sólidos ficam sobre a face apontada), senão o próprio sólido atingido. É o
    /// alvo da borracha, do conta-gotas e do mover — apontar pra placa apaga a placa, apontar
    /// pro chão nu apaga o bloco de chão.
    /// </summary>
    private (int X, int Y, int Z) ContentCell(VoxelPick pick)
        => pick.Place is { } p && AnyAt(p.X, p.Y, p.Z) ? p : pick.Hit;

    /// <summary>
    /// Onde o cursor senta no hover: em cima do PRÓPRIO item atingido quando ele é do mesmo
    /// tipo da brush ativa (apontar pra um toggle com a brush Toggle deixa [ ]/Ctrl+Roda
    /// editarem ELE, não a célula acima); senão, na célula de colocação normal.
    /// </summary>
    private (int X, int Y, int Z) HoverCell(VoxelPick pick)
        => BrushMatchesAt(pick.Hit.X, pick.Hit.Y, pick.Hit.Z) ? pick.Hit : pick.Place ?? pick.Hit;

    /// <summary>True se a célula contém um item do mesmo tipo da brush ativa.</summary>
    private bool BrushMatchesAt(int x, int y, int z) => Brush switch
    {
        EditorBrush.Obstacle => _working.ObstacleSpawns.Any(c => c.X == x && c.Y == y && c.Z == z),
        EditorBrush.Box => _working.BoxSpawns.Any(b => b.X == x && b.Y == y && b.Z == z),
        EditorBrush.Player => _working.PlayerSpawns.Contains((x, y, z)),
        EditorBrush.Objective => _working.ObjectiveSpawns.Contains((x, y, z)),
        EditorBrush.Portal => _working.PortalSpawns.Any(p => p.X == x && p.Y == y && p.Z == z),
        EditorBrush.Plate => _working.PlateSpawns.Any(p => p.X == x && p.Y == y && p.Z == z),
        EditorBrush.Toggle => _working.ToggleSpawns.Any(t => t.X == x && t.Y == y && t.Z == z),
        EditorBrush.TimelessBase => _working.TimelessBaseSpawns.Contains((x, y, z)),
        EditorBrush.Rail => _working.RailSpawns.Any(r => r.X == x && r.Y == y && r.Z == z),
        EditorBrush.PortalBox => _working.PortalBoxSpawns.Any(p => p.X == x && p.Y == y && p.Z == z),
        EditorBrush.BigBox => _working.BigBoxSpawns.Any(b => BigBoxOccupies(b, x, y, z)),
        _ => false,
    };

    private void MoveCursorTo((int X, int Y, int Z) cell)
    {
        CursorX = cell.X;
        CursorY = cell.Y;
        CursorZ = cell.Z;
        ClampCursor();
    }

    private bool LeftPressed(MouseState m)
        => m.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;

    private bool RightPressed(MouseState m)
        => m.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;

    private bool MiddlePressed(MouseState m)
        => m.MiddleButton == ButtonState.Pressed && _prevMouse.MiddleButton == ButtonState.Released;

    // ----- Mover (Ctrl+arrasto) -----

    /// <summary>
    /// Pega o conteúdo da célula apontada: sai da receita (a cena re-materializa sem ele) e
    /// fica "na mão" até o botão ser solto. O snapshot de undo é tirado aqui, então o move
    /// inteiro (tirar + largar) desfaz de uma vez.
    /// </summary>
    private void TryGrab(VoxelPick pick)
    {
        var cell = ContentCell(pick);
        if (!AnyAt(cell.X, cell.Y, cell.Z))
        {
            SetStatus("Nada aqui para mover", warning: true);
            return;
        }

        PushUndo();
        _grabbed = ExtractCell(cell.X, cell.Y, cell.Z);
        _grabbedFrom = cell;
        MoveCursorTo(cell);
        Rebuild();
        SetStatus("Movendo: solte o botao na celula de destino");
    }

    private void DropGrabbed()
    {
        var content = _grabbed;
        _grabbed = null;

        if (InsertCell(content, _grabbedFrom, (CursorX, CursorY, CursorZ)))
        {
            SetStatus($"Movido para ({CursorX},{CursorY},{CursorZ})");
        }
        else
        {
            InsertCell(content, _grabbedFrom, _grabbedFrom); // não coube: devolve pra origem
            SetStatus("Nao coube no destino: item devolvido", warning: true);
        }
        Rebuild();
    }

    /// <summary>Remove tudo o que existe na célula e devolve num Level-clipboard (coords originais).</summary>
    private Level ExtractCell(int x, int y, int z)
    {
        var c = new Level();
        Take(_working.ObstacleSpawns, c.ObstacleSpawns, s => s.X == x && s.Y == y && s.Z == z);
        Take(_working.PlayerSpawns, c.PlayerSpawns, s => s == (x, y, z));
        Take(_working.EnemySpawns, c.EnemySpawns, s => s == (x, y, z));
        Take(_working.ObjectiveSpawns, c.ObjectiveSpawns, s => s == (x, y, z));
        Take(_working.TimelessBaseSpawns, c.TimelessBaseSpawns, s => s == (x, y, z));
        Take(_working.RailSpawns, c.RailSpawns, r => r.X == x && r.Y == y && r.Z == z);
        Take(_working.BoxSpawns, c.BoxSpawns, b => b.X == x && b.Y == y && b.Z == z);
        Take(_working.PortalSpawns, c.PortalSpawns, p => p.X == x && p.Y == y && p.Z == z);
        Take(_working.PlateSpawns, c.PlateSpawns, p => p.X == x && p.Y == y && p.Z == z);
        Take(_working.ToggleSpawns, c.ToggleSpawns, t => t.X == x && t.Y == y && t.Z == z);
        Take(_working.PortalBoxSpawns, c.PortalBoxSpawns, p => p.X == x && p.Y == y && p.Z == z);
        // Caixa grande sai inteira mesmo pega pela segunda célula (a âncora vem junto).
        Take(_working.BigBoxSpawns, c.BigBoxSpawns, b => BigBoxOccupies(b, x, y, z));
        return c;
    }

    private static void Take<T>(List<T> from, List<T> to, Predicate<T> match)
    {
        to.AddRange(from.FindAll(match));
        from.RemoveAll(match);
    }

    /// <summary>
    /// Larga o conteúdo de <see cref="ExtractCell"/> transladado de <paramref name="from"/> pra
    /// <paramref name="to"/>, esvaziando o destino antes (mover pra cima de algo substitui).
    /// False (sem mexer em nada) se uma caixa grande não couber nos limites do destino.
    /// </summary>
    private bool InsertCell(Level content, (int X, int Y, int Z) from, (int X, int Y, int Z) to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;

        foreach (var b in content.BigBoxSpawns)
        {
            var (ox, oz) = b.Axis == BigBoxAxis.X ? (1, 0) : (0, 1);
            if (!InBounds(b.X + dx, b.Y + dy, b.Z + dz) || !InBounds(b.X + dx + ox, b.Y + dy, b.Z + dz + oz))
                return false;
        }

        EraseCell(to.X, to.Y, to.Z);
        foreach (var b in content.BigBoxSpawns)
        {
            var (ox, oz) = b.Axis == BigBoxAxis.X ? (1, 0) : (0, 1);
            EraseCell(b.X + dx + ox, b.Y + dy, b.Z + dz + oz); // segunda célula da caixa grande
        }

        foreach (var s in content.ObstacleSpawns) _working.ObstacleSpawns.Add((s.X + dx, s.Y + dy, s.Z + dz, s.Type));
        foreach (var s in content.PlayerSpawns) _working.PlayerSpawns.Add((s.X + dx, s.Y + dy, s.Z + dz));
        foreach (var s in content.EnemySpawns) _working.EnemySpawns.Add((s.X + dx, s.Y + dy, s.Z + dz));
        foreach (var s in content.ObjectiveSpawns) _working.ObjectiveSpawns.Add((s.X + dx, s.Y + dy, s.Z + dz));
        foreach (var s in content.TimelessBaseSpawns) _working.TimelessBaseSpawns.Add((s.X + dx, s.Y + dy, s.Z + dz));
        foreach (var r in content.RailSpawns) _working.RailSpawns.Add((r.X + dx, r.Y + dy, r.Z + dz, r.Type));
        foreach (var b in content.BoxSpawns) _working.BoxSpawns.Add((b.X + dx, b.Y + dy, b.Z + dz, b.Type));
        foreach (var p in content.PortalSpawns) _working.PortalSpawns.Add((p.X + dx, p.Y + dy, p.Z + dz, p.LevelIndex, p.Completed));
        foreach (var p in content.PlateSpawns) _working.PlateSpawns.Add((p.X + dx, p.Y + dy, p.Z + dz, p.Group));
        foreach (var t in content.ToggleSpawns) _working.ToggleSpawns.Add((t.X + dx, t.Y + dy, t.Z + dz, t.Group, t.SolidByDefault, t.Threshold));
        foreach (var p in content.PortalBoxSpawns) _working.PortalBoxSpawns.Add((p.X + dx, p.Y + dy, p.Z + dz, p.Group));
        foreach (var b in content.BigBoxSpawns) _working.BigBoxSpawns.Add((b.X + dx, b.Y + dy, b.Z + dz, b.Axis));
        return true;
    }

    // ----- Conta-gotas -----

    /// <summary>
    /// Vira a brush (e os parâmetros dela: tipo da caixa, alvo do portal, grupo, threshold,
    /// eixo) no que existe na célula apontada — o jeito rápido de "editar mais um igual àquele".
    /// </summary>
    private void EyedropAt(VoxelPick pick)
    {
        var (x, y, z) = ContentCell(pick);
        int i;

        if ((i = _working.BoxSpawns.FindIndex(b => b.X == x && b.Y == y && b.Z == z)) >= 0)
        {
            Brush = EditorBrush.Box;
            BoxType = _working.BoxSpawns[i].Type;
        }
        else if ((i = _working.PortalSpawns.FindIndex(p => p.X == x && p.Y == y && p.Z == z)) >= 0)
        {
            Brush = EditorBrush.Portal;
            PortalTarget = _working.PortalSpawns[i].LevelIndex;
        }
        else if ((i = _working.PlateSpawns.FindIndex(p => p.X == x && p.Y == y && p.Z == z)) >= 0)
        {
            Brush = EditorBrush.Plate;
            Group = _working.PlateSpawns[i].Group;
        }
        else if ((i = _working.ToggleSpawns.FindIndex(t => t.X == x && t.Y == y && t.Z == z)) >= 0)
        {
            Brush = EditorBrush.Toggle;
            Group = _working.ToggleSpawns[i].Group;
            ToggleSolidByDefault = _working.ToggleSpawns[i].SolidByDefault;
            ToggleThreshold = _working.ToggleSpawns[i].Threshold;
        }
        else if ((i = _working.PortalBoxSpawns.FindIndex(p => p.X == x && p.Y == y && p.Z == z)) >= 0)
        {
            Brush = EditorBrush.PortalBox;
            Group = _working.PortalBoxSpawns[i].Group;
        }
        else if ((i = _working.BigBoxSpawns.FindIndex(b => BigBoxOccupies(b, x, y, z))) >= 0)
        {
            Brush = EditorBrush.BigBox;
            BigBoxAxis = _working.BigBoxSpawns[i].Axis;
        }
        else if (_working.PlayerSpawns.Contains((x, y, z)))
            Brush = EditorBrush.Player;
        else if (_working.ObjectiveSpawns.Contains((x, y, z)))
            Brush = EditorBrush.Objective;
        else if (_working.TimelessBaseSpawns.Contains((x, y, z)))
            Brush = EditorBrush.TimelessBase;
        else if ((i = _working.RailSpawns.FindIndex(r => r.X == x && r.Y == y && r.Z == z)) >= 0)
        {
            Brush = EditorBrush.Rail;
            RailType = _working.RailSpawns[i].Type;
        }
        else if ((i = _working.ObstacleSpawns.FindIndex(c => c.X == x && c.Y == y && c.Z == z)) >= 0)
        {
            Brush = EditorBrush.Obstacle;
            ObstacleType = _working.ObstacleSpawns[i].Type;
        }
        else
        {
            SetStatus("Nada aqui para copiar", warning: true);
            return;
        }

        SetStatus($"Brush copiada: {Brush}");
    }

    /// <summary>True se qualquer spawn da receita mora na célula (sólido ou marcador).</summary>
    private bool AnyAt(int x, int y, int z)
        => _working.ObstacleSpawns.Any(c => c.X == x && c.Y == y && c.Z == z)
        || _working.PlayerSpawns.Contains((x, y, z))
        || _working.EnemySpawns.Contains((x, y, z))
        || _working.ObjectiveSpawns.Contains((x, y, z))
        || _working.TimelessBaseSpawns.Contains((x, y, z))
        || _working.RailSpawns.Any(r => r.X == x && r.Y == y && r.Z == z)
        || _working.BoxSpawns.Any(b => b.X == x && b.Y == y && b.Z == z)
        || _working.PortalSpawns.Any(p => p.X == x && p.Y == y && p.Z == z)
        || _working.PlateSpawns.Any(p => p.X == x && p.Y == y && p.Z == z)
        || _working.ToggleSpawns.Any(t => t.X == x && t.Y == y && t.Z == z)
        || _working.PortalBoxSpawns.Any(p => p.X == x && p.Y == y && p.Z == z)
        || _working.BigBoxSpawns.Any(b => BigBoxOccupies(b, x, y, z));

    // ----- Cursor -----

    private void HandleCursor(KeyboardState k)
    {
        // Cursor no plano do chão com WASD (A/D = X, W/S = Z) e altura com Q/E — convenção de
        // editor de voxel. (O jogo usa W/S pra altura; aqui o mouse é a via principal.)
        if (Pressed(k, Keys.A)) CursorX--;
        if (Pressed(k, Keys.D)) CursorX++;
        if (Pressed(k, Keys.W)) CursorZ--;
        if (Pressed(k, Keys.S)) CursorZ++;
        if (Pressed(k, Keys.Q)) CursorY--;
        if (Pressed(k, Keys.E)) CursorY++;
        ClampCursor();
    }

    private void ClampCursor()
    {
        CursorX = Math.Clamp(CursorX, 0, _working.Width - 1);
        CursorY = Math.Clamp(CursorY, 0, _working.Height - 1);
        CursorZ = Math.Clamp(CursorZ, 0, _working.Depth - 1);
    }

    // ----- Seleção de brush -----

    private void HandleBrushSelection(KeyboardState k)
    {
        if (Pressed(k, Keys.D1)) SelectBrush(EditorBrush.Obstacle);
        if (Pressed(k, Keys.D2)) SelectBrush(EditorBrush.Box);
        if (Pressed(k, Keys.D3)) SelectBrush(EditorBrush.Objective);
        if (Pressed(k, Keys.D4)) SelectBrush(EditorBrush.Portal);
        if (Pressed(k, Keys.D5)) SelectBrush(EditorBrush.Player);
        if (Pressed(k, Keys.D6)) SelectBrush(EditorBrush.Plate);
        if (Pressed(k, Keys.D7)) SelectBrush(EditorBrush.Toggle);
        if (Pressed(k, Keys.D8)) SelectBrush(EditorBrush.TimelessBase);
        if (Pressed(k, Keys.D9)) SelectBrush(EditorBrush.PortalBox);
        if (Pressed(k, Keys.B)) SelectBrush(EditorBrush.BigBox);
        if (Pressed(k, Keys.T)) SelectBrush(EditorBrush.Rail);
        if (Pressed(k, Keys.D0)) SelectBrush(EditorBrush.Eraser);

        // [ ] editam o item sob o cursor (grupo da placa/toggle, alvo do portal); sem nada
        // sob o cursor, ajustam o valor do PRÓXIMO a ser colocado.
        if (Pressed(k, Keys.OemOpenBrackets)) AdjustAtCursor(-1);
        if (Pressed(k, Keys.OemCloseBrackets)) AdjustAtCursor(+1);

        // , . ajustam o threshold do bloco toggle (sob o cursor, ou do próximo a ser colocado).
        if (Pressed(k, Keys.OemComma)) AdjustThreshold(-1);
        if (Pressed(k, Keys.OemPeriod)) AdjustThreshold(+1);
    }

    // Tipos de caixa oferecidos na paleta e no ciclo do [2]. Portal fica de fora: caixa
    // portal é uma brush própria (com o pareamento por grupo).
    private static readonly BoxType[] BoxCycle =
    {
        BoxType.Light, BoxType.Medium, BoxType.Heavy,
        BoxType.Fragile, BoxType.Permanent, BoxType.Magnetic, BoxType.Collectible,
    };

    /// <summary>
    /// Seleciona a brush ativa. O clique na paleta traz o tipo de caixa/obstáculo exato em
    /// <paramref name="box"/>/<paramref name="obstacle"/>; sem ele (teclado), reselecionar a
    /// Caixa ou o Obstáculo cicla o tipo, reselecionar o Toggle inverte o estado de repouso
    /// e a CxGrande inverte o eixo.
    /// </summary>
    private void SelectBrush(EditorBrush brush, BoxType? box = null, ObstacleType? obstacle = null)
    {
        if (box is { } b)
            BoxType = b;
        else if (obstacle is { } o)
            ObstacleType = o;
        else if (brush == EditorBrush.Box && Brush == EditorBrush.Box)
            BoxType = BoxCycle[(Array.IndexOf(BoxCycle, BoxType) + 1) % BoxCycle.Length];
        else if (brush == EditorBrush.Obstacle && Brush == EditorBrush.Obstacle)
            ObstacleType = ObstacleType == ObstacleType.Normal ? ObstacleType.Sticky : ObstacleType.Normal;
        else if (brush == EditorBrush.Toggle && Brush == EditorBrush.Toggle)
            ToggleSolidByDefault = !ToggleSolidByDefault;
        else if (brush == EditorBrush.BigBox && Brush == EditorBrush.BigBox)
            BigBoxAxis = BigBoxAxis == BigBoxAxis.X ? BigBoxAxis.Z : BigBoxAxis.X;
        else if (brush == EditorBrush.Rail && Brush == EditorBrush.Rail)
            RailType = CycleRail(RailType, +1);
        Brush = brush;
    }

    /// <summary>
    /// Muda em <paramref name="delta"/> o número associado ao BRUSH ativo: alvo do portal, ou
    /// grupo da placa/toggle. Se há um item desse mesmo tipo sob o cursor, edita ELE (e
    /// re-materializa pra o rótulo atualizar na hora); senão, ajusta o default que valerá no
    /// próximo a ser colocado. Brushes sem número (obstáculo, caixa, etc.) ignoram o ajuste.
    /// </summary>
    private void AdjustAtCursor(int delta)
    {
        int x = CursorX, y = CursorY, z = CursorZ;

        switch (Brush)
        {
            case EditorBrush.Portal:
                int pi = _working.PortalSpawns.FindIndex(c => c.X == x && c.Y == y && c.Z == z);
                if (pi >= 0)
                {
                    PushUndo();
                    var p = _working.PortalSpawns[pi];
                    p.LevelIndex = Math.Max(0, p.LevelIndex + delta);
                    _working.PortalSpawns[pi] = p;
                    Rebuild();
                }
                else PortalTarget = Math.Max(0, PortalTarget + delta);
                break;

            case EditorBrush.Plate:
                int li = _working.PlateSpawns.FindIndex(c => c.X == x && c.Y == y && c.Z == z);
                if (li >= 0)
                {
                    PushUndo();
                    var pl = _working.PlateSpawns[li];
                    pl.Group = Math.Max(0, pl.Group + delta);
                    _working.PlateSpawns[li] = pl;
                    Rebuild();
                }
                else Group = Math.Max(0, Group + delta);
                break;

            case EditorBrush.PortalBox:
                int pbi = _working.PortalBoxSpawns.FindIndex(c => c.X == x && c.Y == y && c.Z == z);
                if (pbi >= 0)
                {
                    PushUndo();
                    var pb = _working.PortalBoxSpawns[pbi];
                    pb.Group = Math.Max(0, pb.Group + delta);
                    _working.PortalBoxSpawns[pbi] = pb;
                    Rebuild();
                }
                else Group = Math.Max(0, Group + delta);
                break;

            case EditorBrush.Rail:
                int ri = _working.RailSpawns.FindIndex(c => c.X == x && c.Y == y && c.Z == z);
                if (ri >= 0)
                {
                    PushUndo();
                    var r = _working.RailSpawns[ri];
                    r.Type = CycleRail(r.Type, delta);
                    _working.RailSpawns[ri] = r;
                    Rebuild();
                }
                else RailType = CycleRail(RailType, delta);
                break;

            case EditorBrush.Toggle:
                int ti = _working.ToggleSpawns.FindIndex(c => c.X == x && c.Y == y && c.Z == z);
                if (ti >= 0)
                {
                    PushUndo();
                    var t = _working.ToggleSpawns[ti];
                    t.Group = Math.Max(0, t.Group + delta);
                    // Mudou de grupo: o threshold pode passar do nº de placas do novo grupo.
                    t.Threshold = ClampThreshold(t.Group, t.Threshold);
                    _working.ToggleSpawns[ti] = t;
                    Rebuild();
                }
                else Group = Math.Max(0, Group + delta);
                break;
        }
    }

    /// <summary>
    /// Ajusta o threshold do bloco toggle (quantas placas do grupo aciona) em <paramref name="delta"/>:
    /// se há um toggle sob o cursor, edita ELE (e re-materializa); senão, ajusta o default do
    /// próximo a ser colocado. Sempre limitado a [1, nº de placas do grupo].
    /// </summary>
    private void AdjustThreshold(int delta)
    {
        if (Brush != EditorBrush.Toggle)
            return;

        int x = CursorX, y = CursorY, z = CursorZ;
        int ti = _working.ToggleSpawns.FindIndex(c => c.X == x && c.Y == y && c.Z == z);
        if (ti >= 0)
        {
            PushUndo();
            var t = _working.ToggleSpawns[ti];
            t.Threshold = ClampThreshold(t.Group, t.Threshold + delta);
            _working.ToggleSpawns[ti] = t;
            Rebuild();
        }
        else ToggleThreshold = ClampThreshold(Group, ToggleThreshold + delta);
    }

    /// <summary>Limita o threshold a [1, total de placas do grupo] (mínimo 1, mesmo sem placas ainda).</summary>
    private int ClampThreshold(int group, int value)
    {
        int plates = _working.PlateSpawns.Count(pl => pl.Group == group);
        return Math.Clamp(value, 1, Math.Max(1, plates));
    }

    /// <summary>Cicla o tipo de trilho na ordem do enum, pra frente ou pra trás.</summary>
    private static RailType CycleRail(RailType type, int delta)
    {
        var values = Enum.GetValues<RailType>();
        int i = ((int)type + delta) % values.Length;
        return values[(i + values.Length) % values.Length];
    }

    // ----- Colocar / apagar -----

    private void HandlePlacement(KeyboardState k)
    {
        if (Pressed(k, Keys.Space))
        {
            PushUndo();
            Apply();
            Rebuild();
        }
        else if (Pressed(k, Keys.Delete) || Pressed(k, Keys.Back))
        {
            PushUndo();
            EraseCell(CursorX, CursorY, CursorZ);
            Rebuild();
        }
    }

    private void Apply()
    {
        int x = CursorX, y = CursorY, z = CursorZ;
        switch (Brush)
        {
            case EditorBrush.Obstacle:
                RemoveSolidsAt(x, y, z);
                _working.ObstacleSpawns.Add((x, y, z, ObstacleType));
                break;
            case EditorBrush.Box:
                RemoveSolidsAt(x, y, z);
                _working.BoxSpawns.Add((x, y, z, BoxType));
                break;
            case EditorBrush.Player:
                // Player é único no nível: limpa todos antes de fixar o novo.
                _working.PlayerSpawns.Clear();
                RemoveSolidsAt(x, y, z);
                _working.PlayerSpawns.Add((x, y, z));
                break;
            case EditorBrush.Objective:
                RemoveMarkersAt(x, y, z);
                _working.ObjectiveSpawns.Add((x, y, z));
                break;
            case EditorBrush.Portal:
                RemoveMarkersAt(x, y, z);
                _working.PortalSpawns.Add((x, y, z, PortalTarget, false));
                break;
            case EditorBrush.Plate:
                RemoveMarkersAt(x, y, z);
                _working.PlateSpawns.Add((x, y, z, Group));
                break;
            case EditorBrush.TimelessBase:
                RemoveMarkersAt(x, y, z);
                _working.TimelessBaseSpawns.Add((x, y, z));
                break;
            case EditorBrush.Rail:
                RemoveMarkersAt(x, y, z);
                _working.RailSpawns.Add((x, y, z, RailType));
                break;
            case EditorBrush.Toggle:
                RemoveSolidsAt(x, y, z);
                _working.ToggleSpawns.Add((x, y, z, Group, ToggleSolidByDefault, ClampThreshold(Group, ToggleThreshold)));
                break;
            case EditorBrush.PortalBox:
                RemoveSolidsAt(x, y, z);
                _working.PortalBoxSpawns.Add((x, y, z, Group));
                break;
            case EditorBrush.BigBox:
            {
                var (ox, oz) = BigBoxAxis == BigBoxAxis.X ? (1, 0) : (0, 1);
                int bx = x + ox, bz = z + oz;
                if (bx < 0 || bx >= _working.Width || bz < 0 || bz >= _working.Depth)
                {
                    SetStatus("Caixa grande nao cabe aqui (segunda celula fora do grid)", warning: true);
                    break;
                }
                RemoveSolidsAt(x, y, z);
                RemoveSolidsAt(bx, y, bz);
                _working.BigBoxSpawns.Add((x, y, z, BigBoxAxis));
                break;
            }
            case EditorBrush.Eraser:
                EraseCell(x, y, z);
                break;
        }
    }

    private void EraseCell(int x, int y, int z)
    {
        RemoveSolidsAt(x, y, z);
        RemoveMarkersAt(x, y, z);
    }

    private void RemoveSolidsAt(int x, int y, int z)
    {
        _working.ObstacleSpawns.RemoveAll(c => c.X == x && c.Y == y && c.Z == z);
        _working.BoxSpawns.RemoveAll(c => c.X == x && c.Y == y && c.Z == z);
        _working.PlayerSpawns.RemoveAll(c => c.X == x && c.Y == y && c.Z == z);
        _working.EnemySpawns.RemoveAll(c => c.X == x && c.Y == y && c.Z == z);
        _working.ToggleSpawns.RemoveAll(c => c.X == x && c.Y == y && c.Z == z);
        _working.PortalBoxSpawns.RemoveAll(c => c.X == x && c.Y == y && c.Z == z);
        // Caixa grande ocupa DUAS células: mexer em qualquer uma delas remove a unidade inteira,
        // senão sobraria uma "meia" caixa apontando pra uma célula que já tem outra coisa.
        _working.BigBoxSpawns.RemoveAll(c => BigBoxOccupies(c, x, y, z));
    }

    private static bool BigBoxOccupies((int X, int Y, int Z, BigBoxAxis Axis) box, int x, int y, int z)
    {
        if (box.Y != y)
            return false;
        if (box.X == x && box.Z == z)
            return true;
        var (ox, oz) = box.Axis == BigBoxAxis.X ? (1, 0) : (0, 1);
        return box.X + ox == x && box.Z + oz == z;
    }

    private void RemoveMarkersAt(int x, int y, int z)
    {
        _working.ObjectiveSpawns.RemoveAll(c => c.X == x && c.Y == y && c.Z == z);
        _working.PortalSpawns.RemoveAll(c => c.X == x && c.Y == y && c.Z == z);
        _working.PlateSpawns.RemoveAll(c => c.X == x && c.Y == y && c.Z == z);
        _working.TimelessBaseSpawns.RemoveAll(c => c.X == x && c.Y == y && c.Z == z);
        _working.RailSpawns.RemoveAll(c => c.X == x && c.Y == y && c.Z == z);
    }

    // ----- Redimensionar -----

    private void HandleResize(KeyboardState k)
    {
        bool changed = false;
        if (Pressed(k, Keys.D)) changed |= Resize(_working.Width + 1, _working.Height, _working.Depth);
        if (Pressed(k, Keys.A)) changed |= Resize(_working.Width - 1, _working.Height, _working.Depth);
        if (Pressed(k, Keys.S)) changed |= Resize(_working.Width, _working.Height, _working.Depth + 1);
        if (Pressed(k, Keys.W)) changed |= Resize(_working.Width, _working.Height, _working.Depth - 1);
        if (Pressed(k, Keys.E)) changed |= Resize(_working.Width, _working.Height + 1, _working.Depth);
        if (Pressed(k, Keys.Q)) changed |= Resize(_working.Width, _working.Height - 1, _working.Depth);

        if (changed)
        {
            DropOutOfBounds();
            ClampCursor();
            Rebuild();
            SetStatus($"Grid {_working.Width}x{_working.Height}x{_working.Depth}");
            RaiseGridChanged();
        }
    }

    private bool Resize(int w, int h, int d)
    {
        w = Math.Clamp(w, MinSize, MaxSize);
        h = Math.Clamp(h, MinSize, MaxSize);
        d = Math.Clamp(d, MinSize, MaxSize);
        if (w == _working.Width && h == _working.Height && d == _working.Depth)
            return false;
        PushUndo();
        _working.Width = w;
        _working.Height = h;
        _working.Depth = d;
        return true;
    }

    /// <summary>Remove spawns que caíram fora dos novos limites depois de encolher o grid.</summary>
    private void DropOutOfBounds()
    {
        bool Out(int x, int y, int z) => !InBounds(x, y, z);

        _working.ObstacleSpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
        _working.BoxSpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
        _working.PlayerSpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
        _working.EnemySpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
        _working.ObjectiveSpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
        _working.PortalSpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
        _working.PlateSpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
        _working.ToggleSpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
        _working.TimelessBaseSpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
        _working.RailSpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
        _working.PortalBoxSpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
        _working.BigBoxSpawns.RemoveAll(c =>
        {
            var (ox, oz) = c.Axis == BigBoxAxis.X ? (1, 0) : (0, 1);
            return Out(c.X, c.Y, c.Z) || Out(c.X + ox, c.Y, c.Z + oz);
        });
    }

    private bool InBounds(int x, int y, int z)
        => x >= 0 && x < _working.Width && y >= 0 && y < _working.Height && z >= 0 && z < _working.Depth;

    // ----- Validação (solvabilidade) -----

    /// <summary>
    /// Dispara a checagem de solvabilidade em segundo plano sobre um clone da receita atual (o
    /// solver pode levar segundos; um clone deixa o editor seguir editável sem corrida). Enquanto
    /// uma checagem roda, uma nova tecla V é ignorada.
    /// </summary>
    private void Validate()
    {
        if (Validation == EditorValidation.Running)
            return;

        var snapshot = _working.Clone();
        Validation = EditorValidation.Running;
        SetStatus("Validando solvabilidade...");
        _validationTask = Task.Run(() => PuzzleSolver.Solve(snapshot));
    }

    /// <summary>Colhe o resultado da checagem quando o Task termina e traduz pro veredito do HUD.</summary>
    private void PollValidation()
    {
        if (_validationTask is null || !_validationTask.IsCompleted)
            return;

        var task = _validationTask;
        _validationTask = null;

        // Falha da busca (bug do solver, não do design): não derruba o editor.
        if (task.IsFaulted)
        {
            Validation = EditorValidation.Unknown;
            SetStatus("Validacao falhou (ver log)", warning: true);
            Log.Warning(task.Exception, "Editor: checagem de solvabilidade falhou");
            return;
        }

        var result = task.Result;

        if (result.NoObjective)
        {
            Validation = EditorValidation.NoObjective;
            SetStatus("Sem meta: nao ha o que resolver", warning: true);
        }
        else if (result.Solvable)
        {
            Validation = EditorValidation.Solvable;
            SetStatus($"Resolvivel ({result.NodesExplored} nos, {result.Elapsed.TotalSeconds:0.0}s)");
        }
        else if (result.LimitHit)
        {
            Validation = EditorValidation.Unknown;
            SetStatus("Inconclusivo: busca estourou o limite", warning: true);
        }
        else
        {
            Validation = EditorValidation.Unsolvable;
            SetStatus("Sem solucao: o nivel nao tem como ser vencido", warning: true);
        }
    }

    // ----- Arquivos -----

    /// <summary>
    /// Duplica a receita atual num id novo (o próximo livre) e passa a editar a cópia — o jeito
    /// de partir de um nível pronto e criar uma variação sem tocar no original em disco. Reusa o
    /// <see cref="SaveToDisk"/> (grava no novo id, cria alvos de portal faltantes, zera o dirty);
    /// o snapshot de undo deixa o Ctrl+Z voltar a editar o original.
    /// </summary>
    private void Duplicate()
    {
        PushUndo();
        _working.Id = NextFreeId();
        SaveToDisk();
        // Novo id em disco + a sessão ativa mudou de identidade: o Game1 reconstrói a navegação
        // ao sair/pular, senão o cache por id do navigator desincroniza (< > e portais quebram).
        _catalogChanged = true;
        SetStatus($"Duplicado como nivel {_working.Id} — editando a copia");
        Log.Information("Editor: nivel duplicado para {Id}", _working.Id);
    }

    /// <summary>Menor id ainda não usado no repositório (acima do maior existente e do id atual).</summary>
    private int NextFreeId()
    {
        int max = _repo.ListIds().DefaultIfEmpty(-1).Max();
        return Math.Max(max, _working.Id) + 1;
    }

    private void SaveToDisk()
    {
        // Grava no repositório oficial: o mapa editado vira a fonte de verdade daquele id.
        _repo.Save(_working);
        var path = _repo.PathFor(_working.Id);

        // Portais que apontam para ids ainda sem mapa: cria um nível em branco pra cada um,
        // pra o portal não levar a um arquivo inexistente (BuildLevel falharia ao entrar).
        int created = CreateMissingPortalTargets();

        IsDirty = false;
        _pendingConfirm = Confirm.None;
        SetStatus(created > 0
            ? $"Salvo: {path} (+{created} mapa(s) novo(s))"
            : $"Salvo: {path}");
        Log.Information("Editor: nivel salvo em {Path}", path);
    }

    /// <summary>
    /// Para cada destino de portal (LevelIndex) sem arquivo no repositório, grava um nível em
    /// branco com aquele id. Retorna quantos mapas foram criados.
    /// </summary>
    private int CreateMissingPortalTargets()
    {
        int created = 0;
        foreach (var target in _working.PortalSpawns.Select(p => p.LevelIndex).Distinct())
        {
            if (target == _working.Id || _repo.Exists(target))
                continue;

            _repo.Save(BlankLevel(target));
            created++;
            Log.Information("Editor: mapa em branco criado para portal -> nivel {Id}", target);
        }
        return created;
    }

    private void LoadFromDisk()
    {
        if (!_repo.Exists(_working.Id))
        {
            SetStatus($"Sem arquivo: {_repo.PathFor(_working.Id)}", warning: true);
            return;
        }
        if (!ConfirmDestructive(Confirm.Load, "Carregar do disco descarta as alteracoes"))
            return;

        PushUndo();
        _working = _repo.Load(_working.Id);
        IsDirty = false; // recém-carregado do disco: idêntico ao arquivo
        ClampCursor();
        Rebuild();
        SetStatus($"Carregado: {_repo.PathFor(_working.Id)}");
        RaiseGridChanged();
        Log.Information("Editor: nivel carregado de {Path}", _repo.PathFor(_working.Id));
    }

    private void NewBlank()
    {
        if (!ConfirmDestructive(Confirm.New, "Novo em branco descarta as alteracoes"))
            return;

        PushUndo();
        int id = _working.Id;
        _working = BlankLevel(id);
        CursorX = _working.Width / 2;
        CursorZ = _working.Depth / 2;
        CursorY = 1;
        Rebuild();
        SetStatus("Novo nivel em branco");
        RaiseGridChanged();
    }

    private static Level BlankLevel(int id)
    {
        var level = new Level { Id = id, Name = "Custom", Width = 9, Height = 4, Depth = 9 };
        level.FillFloor();
        level.PlayerSpawns.Add((4, 1, 4));
        return level;
    }

    private void SetStatus(string text, bool warning = false)
    {
        Status = text;
        StatusIsWarning = warning;
    }

    private void Rebuild() => _levels.LoadLevel(_session, _working);

    /// <summary>
    /// As dimensões do grid mudaram: reenquadra a câmera do editor (no próximo Update, quando a
    /// viewport estiver disponível) e avisa o Game1 pra reenquadrar a câmera do jogo também.
    /// </summary>
    private void RaiseGridChanged()
    {
        _reframeCam = true;
        GridChanged?.Invoke();
    }

    private bool Pressed(KeyboardState current, Keys key)
        => current.IsKeyDown(key) && _prev.IsKeyUp(key);
}
