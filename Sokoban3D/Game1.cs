using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;
using Sokoban3D.ECS.Systems;
using Sokoban3D.Editor;
using Sokoban3D.Levels;
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
    private bool _editorActive;

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

        _editor = new LevelEditor(_levelManager, _levelRepo);
        // Redimensionar o grid no editor exige reenquadrar a câmera.
        _editor.GridChanged += ReframeCamera;

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
        _editorRenderer = new EditorRenderer(GraphicsDevice, cubes, _hudFont);

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
        bool toggled = Pressed(keyboard, Keys.Tab);
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
                // Célula sob o ponteiro na camada Y atual do cursor (null fora do grid) e botão
                // de brush do HUD sob o ponteiro (null se não estiver sobre nenhum).
                var picked = MousePicker.PickCell(
                    GraphicsDevice.Viewport, _camera.View, _camera.Projection,
                    mouse.X, mouse.Y, Active.Grid, _editor.CursorY);
                var brushButton = _editorRenderer.HitTestBrush(mouse.X, mouse.Y);
                _editor.Update(Active, keyboard, mouse, picked, brushButton);
            }
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // Undo/restart funcionam em qualquer nível — cada sessão tem seu próprio histórico.
        // T = suspender: sai pro pai PRESERVANDO este nível (volta exatamente onde parou).
        if (Pressed(keyboard, Keys.F))
        {
            // Reset total: descarta tudo e recarrega o nível do zero (entidades, grid e histórico).
            _levelManager.FullReset(Active);
            _animationSystem.SnapAll(Active);
        }
        else if (Pressed(keyboard, Keys.R))
            _levelManager.Restart(Active);
        else if (Pressed(keyboard, Keys.Z))
        {
            // Desfaz a última ação. Se o player estava caído, o undo o traz de volta a uma
            // posição segura — então deixa de estar congelado.
            if (Active.History.Undo(Active))
            {
                Active.PlayerFell = false;
                // O undo só restaura posições de peça. A solidez dos toggles é derivada das placas:
                // re-deriva a partir do estado restaurado (o histórico é agnóstico a toggle).
                PressurePlateSystem.Resolve(Active);
                // Undo é instantâneo: encaixa o visual na hora, sem deslizar de volta.
                _animationSystem.SnapAll(Active);
            }
        }
        else if (Pressed(keyboard, Keys.T))
            _navigator.SuspendActive();

        _movementSystem.Update(Active, keyboard);
        _animationSystem.Update(Active, (float)gameTime.ElapsedGameTime.TotalSeconds);

        // Pisar na meta CONCLUI o nível: volta pro pai e reseta este nível (próxima visita
        // começa do zero). Senão, Enter sobre um portal mergulha no nível indicado.
        if (PlayerOnObjective(Active))
            _navigator.CompleteActive();
        else if (Pressed(keyboard, Keys.Enter))
            _navigator.TryEnterPortal();

        _previousKeyboard = keyboard;

        base.Update(gameTime);
    }

    private void ReframeCamera()
    {
        float aspect = GraphicsDevice.Viewport.AspectRatio;
        _camera = new Camera(aspect, Active.Grid.Width, Active.Grid.Depth);
    }

    // ----- Consultas ao mundo -----

    /// <summary>True se a célula do player coincide com a de algum objetivo do nível.</summary>
    private static bool PlayerOnObjective(GameWorld session)
    {
        var playerPos = FindPlayerCell(session);
        if (playerPos is null)
            return false;

        var p = playerPos.Value;
        return session.Spatial.CellWith<Objective>(p.X, p.Y, p.Z) is not null;
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

        base.Draw(gameTime);
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
