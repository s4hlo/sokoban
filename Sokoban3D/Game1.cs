using System.Linq;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;
using Sokoban3D.ECS.Systems;
using Sokoban3D.Editor;
using Sokoban3D.Levels;
using Sokoban3D.Solver;
using Serilog;

namespace Sokoban3D;

public class Game1 : Game
{
    private GraphicsDeviceManager _graphics;
    private LevelManager _levelManager;
    private LevelRepository _levelRepo;
    private LevelCatalog _catalog;
    private MovementSystem _movementSystem;
    private MoveAnimationSystem _animationSystem;
    private RenderSystem _renderSystem;
    private LevelNavigator _navigator;
    private Camera _camera;
    private KeyboardState _previousKeyboard;
    // Mouse do frame anterior, pra detecção de borda de clique e de movimento na lista de níveis.
    private MouseState _previousMouse;

    // Reordenar/duplicar mexeu nos ids em disco → o cache de sessões do navigator ficou obsoleto.
    // A reconstrução (Reload) é adiada pra próxima navegação (FreshJump), pra não resetar quem
    // está no meio de um nível só por ter reordenado.
    private bool _catalogDirty;

    // Editor de níveis: alterna com Tab. Enquanto ativo, os sistemas de jogo ficam pausados.
    private LevelEditor _editor;
    private EditorRenderer _editorRenderer;
    private SpriteFont _hudFont;
    private SpriteBatch _hudBatch;
    private bool _editorActive;

    // Solver-playback (dev tool): alterna com P. Enquanto ativo, o input do player fica
    // pausado e o tool injeta a solução no mesmo Step/Undo do jogo, uma ação por batida.
    private SolverTool _solver;
    private SolverRenderer _solverRenderer;
    private bool _solverActive;

    // Confirmação de duas etapas do P (a busca pode congelar ~30s): 1º P arma, 2º P resolve.
    // Mesmo helper do confirm do editor. Token constante — só há uma ação a confirmar aqui.
    private readonly RepeatConfirm _solveConfirm = new();
    private static readonly object SolveToken = new();

    // Lista de níveis do modo de jogo (tecla M): modal pra navegar e pular pra outro nível sem
    // entrar no editor. Enquanto aberta, o input do player fica suspenso (W/S navega, Enter vai).
    private LevelBrowser _levelBrowser;
    private LevelListRenderer _levelListRenderer;

    // Gravador de certificado (dev tool): acumula as ações da tentativa atual e, na vitória,
    // salva Solutions/level_N.moves pro oráculo de solvabilidade (se ainda não existir).
    private SolutionRecorder _recorder;

    // Sessão ativa: o navigator é dono da pilha/cache de níveis; aqui só se referencia o topo.
    private GameWorld Active => _navigator.Active;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        // Fullscreen em 1080p.
        _graphics.PreferredBackBufferWidth = 1920;
        _graphics.PreferredBackBufferHeight = 1080;
        _graphics.IsFullScreen = true;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger();
    }

    protected override void Initialize()
    {
        _levelManager = new LevelManager();
        _levelRepo = new LevelRepository();
        _catalog = new LevelCatalog(_levelRepo);
        _movementSystem = new MovementSystem();
        _animationSystem = new MoveAnimationSystem();
        _navigator = new LevelNavigator(_levelManager, _catalog);
        _navigator.LevelChanged += ReframeCamera;

        // O log do certificado é por nível: cada troca de sessão só aponta o gravador pro nível
        // ativo (retomar um suspenso continua o log dele); resetar de verdade é outra ação (R/F).
        _recorder = new SolutionRecorder();
        _navigator.LevelChanged += () => _recorder.Track(Active.LevelId);

        _editor = new LevelEditor(_levelManager, _levelRepo);
        // Redimensionar o grid no editor exige reenquadrar a câmera.
        _editor.GridChanged += ReframeCamera;
        // Entrada de texto do SO (respeita layout/shift): alimenta o rename do editor. O editor
        // ignora se não estiver renomeando.
        Window.TextInput += (_, e) => { if (_editorActive) _editor.OnTextInput(e.Character); };

        // O solver-playback reusa o MESMO MovementSystem do jogo: a solução executa pelas
        // regras reais, sem segunda cópia pra divergir.
        _solver = new SolverTool(_levelManager, _movementSystem);

        _levelBrowser = new LevelBrowser();

        Log.Information("Game initialized");

        base.Initialize();
    }

    protected override void LoadContent()
    {
        // O CubeRenderer (preso ao device) é compartilhado entre o render do jogo e o do editor.
        // A máscara de face escurece as bordas de todos os cubos preservando a cor de cada um.
        var faceMask = Content.Load<Texture2D>("dft");
        var cubes = new CubeRenderer(GraphicsDevice, faceMask);
        _renderSystem = new RenderSystem(cubes);
        _hudFont = Content.Load<SpriteFont>("Hud");
        _hudBatch = new SpriteBatch(GraphicsDevice);
        _editorRenderer = new EditorRenderer(GraphicsDevice, cubes, _hudFont);
        _solverRenderer = new SolverRenderer(GraphicsDevice, _hudFont);
        _levelListRenderer = new LevelListRenderer(GraphicsDevice, _hudFont);

        // Começa na raiz da árvore de níveis (um nível como qualquer outro). O navigator
        // dispara LevelChanged, que reposiciona a câmera.
        _navigator.EnterRoot();
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();
        var mouse = Mouse.GetState();

        // Esc nunca encerra o jogo: ele recua um nível de contexto (fecha a lista, senão sai do
        // editor). Só o Back do gamepad (ou fechar a janela) encerra de fato.
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed)
            Exit();

        // Menu de listagem (M): o ÚNICO menu, aberto tanto jogando quanto editando. Enquanto
        // visível é modal — captura todo o input (navega/reordena/escolhe) e a cena atrás (jogo
        // ou editor) segue desenhada. Escolher um nível torna-o ativo (edita se você tava no
        // editor via Tab; joga se tava jogando).
        if (_levelBrowser.Visible)
        {
            HandleLevelMenu(keyboard, mouse);
            _animationSystem.Update(Active, (float)gameTime.ElapsedGameTime.TotalSeconds);
            _previousKeyboard = keyboard;
            _previousMouse = mouse;
            base.Update(gameTime);
            return;
        }
        // M abre o menu de qualquer modo, menos durante o rename do editor ou o playback do solver.
        if (Pressed(keyboard, Keys.M) && !(_editorActive && _editor.IsModal) && !_solverActive)
        {
            _levelBrowser.Open(_levelRepo, Active.LevelId);
            _previousKeyboard = keyboard;
            _previousMouse = mouse;
            base.Update(gameTime);
            return;
        }

        // Modal do editor aberto (rename): Esc/Tab ficam com o editor (fecham o modal), não trocam
        // de modo aqui.
        bool editorModal = _editorActive && _editor.IsModal;

        // Tab alterna jogar/editar; Esc (já no editor e fora de modal) volta a jogar. Esc nunca
        // ENTRA no editor — só Tab. Mutuamente exclusivo com o solver (P).
        bool escLeavesEditor = _editorActive && !editorModal && Pressed(keyboard, Keys.Escape);
        bool toggled = !_solverActive && !editorModal && (Pressed(keyboard, Keys.Tab) || escLeavesEditor);
        if (toggled)
        {
            _editorActive = !_editorActive;
            if (_editorActive)
                _editor.Enter(Active, keyboard, mouse, GraphicsDevice.Viewport);
            else
            {
                // Ao voltar a jogar: se duplicou (mudou os ids em disco), grava o nível editado e
                // marca o cache do navigator como obsoleto — o FreshJump reconstrói e retoma nele.
                var editedLevel = _editor.Working;
                int editedId = editedLevel?.Id ?? LevelCatalog.RootId;
                _editor.Exit(Active);
                if (_editor.ConsumeCatalogChanged())
                {
                    if (editedLevel != null)
                        _levelRepo.Save(editedLevel);
                    _catalogDirty = true;
                }
                if (_catalogDirty)
                    FreshJump(editedId);
            }
        }

        if (_editorActive)
        {
            // No editor os sistemas de jogo ficam pausados; só o editor processa input. O editor
            // faz o próprio picking; daqui vai só o botão de brush do HUD sob o ponteiro.
            if (!toggled)
            {
                var brushButton = _editorRenderer.HitTestBrush(mouse.X, mouse.Y);
                _editor.Update(Active, keyboard, mouse, GraphicsDevice.Viewport, brushButton);
            }
            _previousKeyboard = keyboard;
            _previousMouse = mouse;
            base.Update(gameTime);
            return;
        }

        // P dispara a busca do solver (dev tool). Como a busca pode CONGELAR o jogo por até ~30s,
        // ela pede confirmação de duas etapas (mesmo RepeatConfirm do editor): o 1º P só arma (o
        // HUD avisa), o 2º P confirma e resolve. Mover cancela o armado. C é o irmão do P, mas toca
        // o CERTIFICADO gravado (rápido, sem congelar), então entra direto. Esc/P (ou C) sai do modo.
        bool searchKey = Pressed(keyboard, Keys.P);
        bool certificateKey = !searchKey && Pressed(keyboard, Keys.C);
        bool escLeavesSolver = _solverActive && Pressed(keyboard, Keys.Escape);
        if (searchKey)
        {
            if (_solverActive)
            {
                _solverActive = false;
                _solver.Exit(Active);
            }
            else if (_solveConfirm.Press(SolveToken)) // 2ª batida: confirma a busca
            {
                _solverActive = true;
                _solver.Enter(Active);
                _recorder.Reset(Active.LevelId);
                _animationSystem.SnapAll(Active);
            }
            // 1ª batida: só armou — o HUD mostra a dica, o jogo segue jogável
        }
        else if (certificateKey || escLeavesSolver)
        {
            _solveConfirm.Clear(); // um C ou Esc descarta o "P armado" pendente
            _solverActive = !_solverActive;
            if (_solverActive)
            {
                _solver.EnterCertificate(Active);
                _recorder.Reset(Active.LevelId);
                _animationSystem.SnapAll(Active);
            }
            else
                _solver.Exit(Active);
        }

        if (_solverActive)
        {
            // Playback: o tool injeta as ações no engine; o input do player fica pausado.
            // Animação e placas seguem rodando (é um turno normal do jogo). A conclusão pela
            // meta fica SUSPENSA — dá pra ver o estado final; sair com P e pisar de novo conclui.
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _solver.Update(Active, dt);
            PressurePlateSystem.Resolve(Active);
            _animationSystem.Update(Active, dt);
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // Reverso/restart funcionam em qualquer nível — cada sessão tem seu próprio histórico.
        if (Pressed(keyboard, Keys.F))
        {
            // Reset total: descarta tudo e recarrega o nível do zero (entidades, grid e histórico).
            _levelManager.FullReset(Active);
            _animationSystem.SnapAll(Active);
            _recorder.Reset(Active.LevelId);
        }
        else if (Pressed(keyboard, Keys.R))
        {
            _levelManager.Restart(Active);
            _recorder.Reset(Active.LevelId);
        }
        else if (Pressed(keyboard, Keys.Z))
        {
            // Undo canônico (regras no MovementSystem.Undo). Não snapa: deixa o RenderPosition
            // defasado pra o MoveAnimationSystem deslizar as peças de volta, igual a um movimento
            // normal. Quem voltou atravessando um portal ganha a animação de teleporte
            // reconstruída do mundo (o histórico não guarda portal) — parte de render, só aqui.
            if (_movementSystem.Undo(Active))
            {
                _movementSystem.AnimateUndoTeleports(Active, Active.History.LastReverted);
                _recorder.RecordUndo();
            }
        }
        // T = suspender: sai pro pai preservando este nível (volta exatamente onde parou).
        else if (Pressed(keyboard, Keys.T))
            _navigator.SuspendActive();
        // Ponto/vírgula saltam pro próximo/anterior nível na ordem dos ids (com wrap).
        else if (Pressed(keyboard, Keys.OemPeriod))
            JumpRelative(+1);
        else if (Pressed(keyboard, Keys.OemComma))
            JumpRelative(-1);

        var stepped = _movementSystem.Update(Active, keyboard);
        _recorder.RecordStep(stepped.Dx, stepped.Dz);

        // Mover cancela o "P armado" (como uma edição cancela o confirm do editor).
        if (stepped.Dx != 0 || stepped.Dz != 0)
            _solveConfirm.Clear();

        // Placas de pressão são estado derivado da posição das peças: re-deriva todo frame,
        // independente do que mexeu nas posições (movimento, undo ou nada).
        PressurePlateSystem.Resolve(Active);

        _animationSystem.Update(Active, (float)gameTime.ElapsedGameTime.TotalSeconds);

        // Pisar na meta CONCLUI o nível: volta pro pai e reseta este nível (próxima visita
        // começa do zero). A tentativa vencedora vira certificado de solvabilidade do oráculo,
        // se o nível ainda não tem um. Senão, Enter sobre um portal mergulha no nível indicado.
        if (PlayerOnObjective(Active))
        {
            _recorder.SaveOnWin(Active);
            _navigator.CompleteActive();
        }
        else if (Pressed(keyboard, Keys.Enter))
            _navigator.TryEnterPortal();

        _previousKeyboard = keyboard;

        base.Update(gameTime);
    }

    /// <summary>
    /// Input do menu de listagem aberto (modal, o mesmo jogando ou editando): hover do mouse (com
    /// movimento) ou W/S navegam; Shift+W/S reordena; clique/Enter escolhe (torna o nível ativo);
    /// M/Esc fecham.
    /// </summary>
    private void HandleLevelMenu(KeyboardState keyboard, MouseState mouse)
    {
        int? hoverRow = _levelListRenderer.HitTestRow(mouse.X, mouse.Y);

        // Mouse: passar por cima seleciona (só quando o ponteiro move, pra não brigar com o
        // teclado); clique escolhe.
        if (hoverRow is int row)
        {
            if (mouse.X != _previousMouse.X || mouse.Y != _previousMouse.Y)
                _levelBrowser.Select(row);
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
            {
                _levelBrowser.Select(row);
                ChooseLevel();
                return;
            }
        }

        // W/S navega; Shift+W/S reordena (troca ids em disco).
        bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        if (Pressed(keyboard, Keys.W) || Pressed(keyboard, Keys.Up))
        {
            if (shift) ReorderSelected(-1); else _levelBrowser.MoveUp();
        }
        else if (Pressed(keyboard, Keys.S) || Pressed(keyboard, Keys.Down))
        {
            if (shift) ReorderSelected(+1); else _levelBrowser.MoveDown();
        }

        if (Pressed(keyboard, Keys.Enter))
            ChooseLevel();
        else if (Pressed(keyboard, Keys.M) || Pressed(keyboard, Keys.Escape))
            _levelBrowser.Close();
    }

    /// <summary>
    /// Escolher um nível: fecha o menu e torna o nível ativo. No editor, sai pro jogo (Tab pra
    /// editar depois), gravando/reconstruindo se o catálogo mudou. O FreshJump reconstrói o cache
    /// do navigator se um reorder o deixou obsoleto.
    /// </summary>
    private void ChooseLevel()
    {
        if (_levelBrowser.SelectedId is not int id)
        {
            _levelBrowser.Close();
            return;
        }
        _levelBrowser.Close();

        if (_editorActive)
        {
            var editedLevel = _editor.Working;
            _editor.Exit(Active);
            _editorActive = false;
            if (_editor.ConsumeCatalogChanged())
            {
                if (editedLevel != null)
                    _levelRepo.Save(editedLevel);
                _catalogDirty = true;
            }
        }
        FreshJump(id);
    }

    /// <summary>
    /// Reordena: troca o nível selecionado com o vizinho (ids em disco via
    /// <see cref="LevelRepository.SwapIds"/>), remonta o menu e marca o cache do navigator pra
    /// reconstruir na próxima navegação.
    /// </summary>
    private void ReorderSelected(int dir)
    {
        var items = _levelBrowser.Items;
        int i = _levelBrowser.Selection, j = i + dir;
        if (j < 0 || j >= items.Count)
            return;

        _levelRepo.SwapIds(items[i].Id, items[j].Id);
        _catalogDirty = true;
        _levelBrowser.Refresh(_levelRepo);
        _levelBrowser.Select(j); // a seleção segue o item movido
    }

    /// <summary>
    /// Navega pro nível <paramref name="id"/>, reconstruindo antes o cache do navigator se um
    /// reorder/duplicar o deixou obsoleto (<see cref="_catalogDirty"/>). Fonte única de troca de
    /// nível — usada pelo menu, pelos atalhos , . e pela saída do editor.
    /// </summary>
    private void FreshJump(int id)
    {
        if (_catalogDirty)
        {
            _navigator.Reload();
            _catalogDirty = false;
        }
        _navigator.JumpTo(id);
    }

    /// <summary>
    /// Salta <paramref name="step"/> posições na lista ordenada de ids do repositório, com
    /// wrap nas pontas. Ignora a árvore de portais — é um atalho de navegação direta.
    /// </summary>
    private void JumpRelative(int step)
    {
        var ids = _levelRepo.ListIds().OrderBy(id => id).ToList();
        if (ids.Count == 0)
            return;

        int idx = ids.IndexOf(Active.LevelId);
        // Nível atual fora do repositório (não deve acontecer): começa da primeira posição.
        if (idx < 0)
            idx = 0;

        FreshJump(ids[(idx + step + ids.Count) % ids.Count]);
    }

    private void ReframeCamera()
    {
        float aspect = GraphicsDevice.Viewport.AspectRatio;
        _camera = new Camera(aspect, Active.Grid.Width, Active.Grid.Depth);
    }

    // ----- Consultas ao mundo -----

    /// <summary>
    /// True se a célula do player coincide com a de algum objetivo do nível E não há coletável
    /// pendente (ver <see cref="Collectibles"/>) — com algum ainda por coletar, a meta fica
    /// desabilitada e pisar nela não faz nada.
    /// </summary>
    private static bool PlayerOnObjective(GameWorld session)
    {
        var playerPos = FindPlayerCell(session);
        if (playerPos is null)
            return false;

        var p = playerPos.Value;
        return session.Spatial.CellWith<Objective>(p.X, p.Y, p.Z) is not null
            && Collectibles.AllCollected(session);
    }

    private static GridPosition? FindPlayerCell(GameWorld session)
    {
        var player = session.Spatial.First<Player>();
        return player is null ? null : session.World.Get<GridPosition>(player.Value);
    }

    // Detecção de borda: tecla recém-pressionada neste frame.
    private bool Pressed(KeyboardState current, Keys key)
        => current.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        // O SpriteBatch do HUD do editor deixa o BlendState em AlphaBlend; restaura o opaco
        // pra cena 3D do próximo frame não herdar transparência.
        GraphicsDevice.BlendState = BlendState.Opaque;
        // Descarta a face traseira (convenção do MonoGame). O cubo é enrolado pra isso em
        // CubeRenderer.BuildCube, então só as faces externas são desenhadas.
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

        // No editor a cena usa a câmera orbitável do próprio editor; fora dele, a câmera fixa.
        Matrix view = _editorActive ? _editor.View : _camera.View;
        Matrix projection = _editorActive ? _editor.Projection : _camera.Projection;
        _renderSystem.Draw(Active, view, projection);

        // Overlay do editor (cursor + HUD) por cima da cena — escondido quando o menu de listagem
        // está aberto (ele é o modal por cima de tudo).
        if (_editorActive)
        {
            if (!_levelBrowser.Visible)
                _editorRenderer.Draw(Active, _editor, view, projection);
        }
        else if (!_levelBrowser.Visible)
            DrawLevelHud();

        // HUD do solver-playback por cima da cena.
        if (_solverActive)
            _solverRenderer.Draw(_solver);

        // Menu de listagem (M): o modal único, por cima de tudo — jogando ou editando.
        if (_levelBrowser.Visible)
            _levelListRenderer.Draw(_levelBrowser.Items, _levelBrowser.Selection, Active.LevelId);

        base.Draw(gameTime);
    }

    /// <summary>HUD do jogo: nível atual (id + nome) no canto superior esquerdo e o atalho de troca.</summary>
    private void DrawLevelHud()
    {
        string baseLabel = Active.LevelId == LevelCatalog.RootId
            ? "Nivel 0 (raiz)"
            : $"Nivel {Active.LevelId}";

        // Nome do design (mesmo mostrado no editor), quando houver.
        string name = Active.CurrentLevel?.Name;
        string label = string.IsNullOrEmpty(name) ? baseLabel : $"{baseLabel} - {name}";

        _hudBatch.Begin();
        DrawShadowed(label, new Vector2(16, 12), Color.White);
        DrawShadowed("< > troca nivel   M lista", new Vector2(16, 12 + _hudFont.LineSpacing), Color.LightGray * 0.8f);
        // Solver armado: avisa que o 2º P vai rodar a busca (que pode congelar).
        if (_solveConfirm.Pending)
            DrawShadowed("P de novo: resolver (pode congelar ~30s) · mover cancela",
                new Vector2(16, 12 + _hudFont.LineSpacing * 2), new Color(245, 165, 70));
        _hudBatch.End();
    }

    private void DrawShadowed(string text, Vector2 pos, Color color)
    {
        _hudBatch.DrawString(_hudFont, text, pos + Vector2.One, Color.Black * 0.7f);
        _hudBatch.DrawString(_hudFont, text, pos, color);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _navigator.Dispose();
            Log.CloseAndFlush();
        }
        base.Dispose(disposing);
    }
}
