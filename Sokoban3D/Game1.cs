using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Sokoban3D.Core;
using Sokoban3D.ECS.Systems;
using Sokoban3D.Levels;
using Serilog;

namespace Sokoban3D;

public class Game1 : Game
{
    private GraphicsDeviceManager _graphics;
    private GameWorld _gameWorld;
    private LevelManager _levelManager;
    private MovementSystem _movementSystem;
    private MoveAnimationSystem _animationSystem;
    private RenderSystem _renderSystem;
    private Camera _camera;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        // Setup logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger();
    }

    protected override void Initialize()
    {
        // Grid plano: uma única camada (altura 1) no plano X/Z.
        _gameWorld = new GameWorld(8, 1, 8);
        _levelManager = new LevelManager(_gameWorld);
        _movementSystem = new MovementSystem(_gameWorld);
        _animationSystem = new MoveAnimationSystem(_gameWorld);

        Log.Information("Game initialized");

        base.Initialize();
    }

    protected override void LoadContent()
    {
        // Level básico: um player e uma caixa pra empurrar.
        var testLevel = new Level
        {
            Name = "Level 1",
            Width = 8,
            Height = 1,
            Depth = 8
        };
        testLevel.PlayerSpawns.Add((3, 0, 3));
        testLevel.BoxSpawns.Add((4, 0, 3, 1));
        testLevel.BoxSpawns.Add((2, 0, 5, 1));
        testLevel.BoxSpawns.Add((5, 0, 5, 1));

        _levelManager.LoadLevel(testLevel);
        Log.Information("Level loaded: {LevelName}", testLevel.Name);

        float aspect = GraphicsDevice.Viewport.AspectRatio;
        _camera = new Camera(aspect, Vector3.Zero);
        _renderSystem = new RenderSystem(_gameWorld, GraphicsDevice);
    }

    protected override void Update(GameTime gameTime)
    {
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        _movementSystem.Update(Keyboard.GetState());
        _animationSystem.Update((float)gameTime.ElapsedGameTime.TotalSeconds);

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

        _renderSystem.Draw(_camera.View, _camera.Projection);

        base.Draw(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gameWorld?.Dispose();
            Log.CloseAndFlush();
        }
        base.Dispose(disposing);
    }
}
