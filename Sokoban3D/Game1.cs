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

        // O solver-playback reusa o MESMO MovementSystem do jogo: a solução executa pelas
        // regras reais, sem segunda cópia pra divergir.
        _solver = new SolverTool(_levelManager, _movementSystem);

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

        // Começa na raiz da árvore de níveis (um nível como qualquer outro). O navigator
        // dispara LevelChanged, que reposiciona a câmera.
        _navigator.EnterRoot();
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();
        var mouse = Mouse.GetState();

        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || keyboard.IsKeyDown(Keys.Escape))
            Exit();

        // Tab alterna entre jogar e editar a sessão ativa. Ao entrar, o editor recebe o teclado
        // e o mouse atuais pra a detecção de borda não disparar uma brush no mesmo frame.
        // Mutuamente exclusivo com o solver (P).
        bool toggled = !_solverActive && Pressed(keyboard, Keys.Tab);
        if (toggled)
        {
            _editorActive = !_editorActive;
            if (_editorActive)
                _editor.Enter(Active, keyboard, mouse);
            else
                _editor.Exit(Active);
        }

        if (_editorActive)
        {
            // No editor os sistemas de jogo ficam pausados; só o editor processa input.
            if (!toggled)
            {
                // O editor faz o próprio picking (raycast contra a cena); daqui vai só a câmera
                // e o botão de brush do HUD sob o ponteiro (null se não estiver sobre nenhum).
                var brushButton = _editorRenderer.HitTestBrush(mouse.X, mouse.Y);
                _editor.Update(Active, keyboard, mouse,
                    GraphicsDevice.Viewport, _camera.View, _camera.Projection, brushButton);
            }
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // P alterna o solver-playback (dev tool). Ao entrar, o nível reseta pro estado de
        // receita (o solver resolve a partir do zero) e a solução executa sozinha.
        // C é o irmão do P: mesmo playback, mas tocando o CERTIFICADO gravado
        // (Solutions/level_N.moves) em vez de buscar. Qualquer um dos dois sai do modo.
        bool searchKey = Pressed(keyboard, Keys.P);
        bool certificateKey = !searchKey && Pressed(keyboard, Keys.C);
        if (searchKey || certificateKey)
        {
            _solverActive = !_solverActive;
            if (_solverActive)
            {
                if (searchKey)
                    _solver.Enter(Active);
                else
                    _solver.EnterCertificate(Active);
                // O solver também reseta o mundo pro estado de receita (FullReset) — o log da
                // tentativa reseta junto, senão jogadas manuais de antes vazam pro certificado.
                _recorder.Reset(Active.LevelId);
                // FullReset é instantâneo: encaixa o render sem deslizar, como no F.
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

        _navigator.JumpTo(ids[(idx + step + ids.Count) % ids.Count]);
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

        _renderSystem.Draw(Active, _camera.View, _camera.Projection);

        // Overlay do editor (cursor + HUD) por cima da cena.
        if (_editorActive)
            _editorRenderer.Draw(Active, _editor, _camera.View, _camera.Projection);
        else
            DrawLevelHud();

        // HUD do solver-playback por cima da cena.
        if (_solverActive)
            _solverRenderer.Draw(_solver);

        base.Draw(gameTime);
    }

    /// <summary>HUD do jogo: nível atual no canto superior esquerdo e o atalho de troca.</summary>
    private void DrawLevelHud()
    {
        string label = Active.LevelId == LevelCatalog.RootId
            ? "Nivel 0 (raiz)"
            : $"Nivel {Active.LevelId}";

        _hudBatch.Begin();
        DrawShadowed(label, new Vector2(16, 12), Color.White);
        DrawShadowed("< > troca nivel", new Vector2(16, 12 + _hudFont.LineSpacing), Color.LightGray * 0.8f);
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
