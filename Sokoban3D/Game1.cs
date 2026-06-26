using System.Collections.Generic;
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
    private Camera _camera;
    private KeyboardState _previousKeyboard;

    // Pilha de navegação na árvore de níveis: cada nível é uma sessão isolada (não há
    // hub especial — a raiz é só mais um nível). O topo é a sessão ativa; entrar num
    // portal empilha um filho; concluir o objetivo descarta o topo. O nível-pai fica
    // preservado intacto enquanto você está num filho.
    private readonly Stack<GameWorld> _stack = new();

    // Cache de sessões por id de nível. Cada nível visitado mantém sua sessão viva aqui,
    // então reentrar restaura o estado (caixas onde você deixou) em vez de reconstruir.
    // A pilha guarda só o caminho atual na árvore; a preservação vem deste cache.
    private readonly Dictionary<int, GameWorld> _sessions = new();

    private GameWorld Active => _stack.Peek();

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

        Log.Information("Game initialized");

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _renderSystem = new RenderSystem(GraphicsDevice);

        // Começa na raiz da árvore de níveis (um nível como qualquer outro).
        _stack.Push(OpenLevel(LevelCatalog.RootId));
        ReframeCamera();
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
                Active.PlayerFell = false;
        }
        else if (Pressed(keyboard, Keys.T))
            SuspendLevel();

        _movementSystem.Update(Active, keyboard);
        _animationSystem.Update(Active, (float)gameTime.ElapsedGameTime.TotalSeconds);

        // Pisar na meta CONCLUI o nível: volta pro pai e reseta este nível (próxima visita
        // começa do zero). Senão, Enter sobre um portal mergulha no nível indicado.
        if (PlayerOnObjective(Active))
            CompleteLevel();
        else if (Pressed(keyboard, Keys.Enter))
            TryEnterPortal();

        _previousKeyboard = keyboard;

        base.Update(gameTime);
    }

    // ----- Pilha de níveis -----

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

    /// <summary>No nível atual: se o player estiver sobre um portal, mergulha no nível dele.</summary>
    private void TryEnterPortal()
    {
        var playerPos = FindPlayerCell(Active);
        if (playerPos is null)
            return;

        var p = playerPos.Value;
        var portalEntity = Active.Spatial.CellWith<LevelPortal>(p.X, p.Y, p.Z);
        if (portalEntity is null)
            return;

        int target = Active.World.Get<LevelPortal>(portalEntity.Value).LevelIndex;
        _stack.Push(OpenLevel(target));
        ReframeCamera();
        Log.Information("Entered level {Id}", target);
    }

    /// <summary>
    /// Conclui o nível ativo (chegou na meta): marca como concluído e volta ao pai, DESCARTANDO
    /// a sessão. Como sai do cache, a próxima visita reconstrói o nível do zero — estado e
    /// histórico limpos. Pra sair preservando o nível, use o T (<see cref="SuspendLevel"/>).
    /// </summary>
    private void CompleteLevel()
    {
        // A raiz da árvore não tem pai pra onde voltar; guarda genérica (ela também não
        // tem objetivo, então na prática nunca chega aqui).
        if (_stack.Count <= 1)
            return;

        var done = _stack.Pop();
        _sessions.Remove(done.LevelId);
        done.Dispose();
        _catalog.MarkCompleted(done.LevelId);
        Log.Information("Level {Id} completed (reset)", done.LevelId);

        // Atualiza os portais do pai pra refletir a conclusão (o que acabou de ficar verde).
        RefreshPortals(Active);
        ReframeCamera();
    }

    /// <summary>
    /// Suspende o nível ativo (tecla T): volta ao pai sem concluir, MANTENDO a sessão no cache.
    /// Reentrar restaura o nível exatamente onde parou (caixas e histórico). É o oposto de
    /// concluir pela meta, que reseta. Na raiz não faz nada (não há pai pra onde voltar).
    /// </summary>
    private void SuspendLevel()
    {
        if (_stack.Count <= 1)
            return;

        var suspended = _stack.Pop(); // permanece em _sessions, preservado
        Log.Information("Level {Id} suspended (preserved)", suspended.LevelId);

        RefreshPortals(Active);
        ReframeCamera();
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
            // O cache é dono de todas as sessões (a pilha só referencia um subconjunto).
            foreach (var session in _sessions.Values)
                session.Dispose();
            _sessions.Clear();
            _stack.Clear();
            Log.CloseAndFlush();
        }
        base.Dispose(disposing);
    }
}
