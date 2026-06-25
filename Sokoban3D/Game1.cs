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
    private GameWorld _gameWorld;
    private LevelManager _levelManager;
    private MovementSystem _movementSystem;
    private MoveAnimationSystem _animationSystem;
    private RenderSystem _renderSystem;
    private Camera _camera;
    private History _history;
    private KeyboardState _previousKeyboard;

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
        _history = new History();
        _levelManager = new LevelManager(_gameWorld);
        _movementSystem = new MovementSystem(_gameWorld, _history);
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
        // Pesada à direita do player (empurra 1 só).
        testLevel.BoxSpawns.Add((4, 0, 3, BoxType.Heavy));
        // Duas médias em fila abaixo do player (empurra as duas de uma vez).
        testLevel.BoxSpawns.Add((3, 0, 5, BoxType.Medium));
        testLevel.BoxSpawns.Add((3, 0, 6, BoxType.Medium));
        testLevel.BoxSpawns.Add((3, 0, 4, BoxType.Medium));
        // Leves em fila (carregadas de graça, mesmo várias).
        testLevel.BoxSpawns.Add((5, 0, 5, BoxType.Light));
        testLevel.BoxSpawns.Add((6, 0, 5, BoxType.Light));
        // Frágeis: empurram como leve, mas quebram contra algo que não move.
        // Acima do player (W três vezes empurra contra a parede de cima -> quebra).
        testLevel.BoxSpawns.Add((3, 0, 0, BoxType.Fragile));
        // À esquerda do player (A empurra até a borda; lá ela quebra).
        testLevel.BoxSpawns.Add((1, 0, 3, BoxType.Fragile));
        // Permanente (verde): empurra como pesada; o undo (Z) não a reverte, só o R.
        testLevel.BoxSpawns.Add((3, 0, 2, BoxType.Permanent));

        _levelManager.LoadLevel(testLevel);
        Log.Information("Level loaded: {LevelName}", testLevel.Name);

        float aspect = GraphicsDevice.Viewport.AspectRatio;
        _camera = new Camera(aspect, Vector3.Zero);
        _renderSystem = new RenderSystem(_gameWorld, GraphicsDevice);
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();

        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || keyboard.IsKeyDown(Keys.Escape))
            Exit();

        if (Pressed(keyboard, Keys.R))
        {
            // Restart empilha um snapshot no histórico, então o próprio R pode ser desfeito com Z.
            _levelManager.Restart(_history);
        }
        else if (Pressed(keyboard, Keys.Z))
        {
            _history.Undo(_gameWorld);
        }

        _movementSystem.Update(keyboard);
        _animationSystem.Update((float)gameTime.ElapsedGameTime.TotalSeconds);

        _previousKeyboard = keyboard;

        base.Update(gameTime);
    }

    // Detecção de borda: tecla recém-pressionada neste frame.
    private bool Pressed(KeyboardState current, Keys key)
        => current.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);

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
