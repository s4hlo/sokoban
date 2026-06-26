using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;
using Sokoban3D.ECS.Systems;
using Sokoban3D.Levels;
using Serilog;

namespace Sokoban3D;

public class Game1 : Game
{
    private GraphicsDeviceManager _graphics;
    private LevelManager _levelManager;
    private LevelCatalog _catalog;
    private MovementSystem _movementSystem;
    private MoveAnimationSystem _animationSystem;
    private RenderSystem _renderSystem;
    private LevelNavigator _navigator;
    private Camera _camera;
    private KeyboardState _previousKeyboard;

    // Sessão ativa: o navigator é dono da pilha/cache de níveis; aqui só se referencia o topo.
    private GameWorld Active => _navigator.Active;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
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
        _catalog = new LevelCatalog();
        _movementSystem = new MovementSystem();
        _animationSystem = new MoveAnimationSystem();
        _navigator = new LevelNavigator(_levelManager, _catalog);
        _navigator.LevelChanged += ReframeCamera;

        Log.Information("Game initialized");

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _renderSystem = new RenderSystem(GraphicsDevice);

        // Começa na raiz da árvore de níveis (um nível como qualquer outro). O navigator
        // dispara LevelChanged, que reposiciona a câmera.
        _navigator.EnterRoot();
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();

        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || keyboard.IsKeyDown(Keys.Escape))
            Exit();

        // Undo/restart funcionam em qualquer nível — cada sessão tem seu próprio histórico.
        // T = suspender: sai pro pai PRESERVANDO este nível (volta exatamente onde parou).
        if (Pressed(keyboard, Keys.R))
            _levelManager.Restart(Active);
        else if (Pressed(keyboard, Keys.Z))
        {
            // Desfaz a última ação. Se o player estava caído, o undo o traz de volta a uma
            // posição segura — então deixa de estar congelado.
            if (Active.History.Undo(Active))
            {
                Active.PlayerFell = false;
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
        // Descarta a face traseira (convenção do MonoGame). O cubo é enrolado pra isso em
        // CubeRenderer.BuildCube, então só as faces externas são desenhadas.
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

        _renderSystem.Draw(Active, _camera.View, _camera.Projection);

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
