using System;
using System.IO;
using Microsoft.Xna.Framework.Input;
using Serilog;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;
using Sokoban3D.Levels;

namespace Sokoban3D.Editor;

/// <summary>
/// Editor de níveis in-game. Edita um <see cref="Level"/> (a receita, fonte de verdade do
/// design) e a cada mudança re-materializa a sessão ativa via <see cref="LevelManager.LoadLevel"/>
/// — reaproveitando todo o ECS e o render do jogo. O cursor e o HUD ficam por conta do
/// <see cref="EditorRenderer"/>; aqui mora o estado e a lógica de edição.
/// </summary>
public class LevelEditor
{
    private const int MinSize = 1;
    private const int MaxSize = 32;

    private readonly LevelManager _levels;

    // Receita em edição (clone do nível ativo). Null fora do modo editor.
    private Level _working;
    private GameWorld _session;
    private KeyboardState _prev;

    // ----- Estado exposto pro renderer -----
    public int CursorX { get; private set; }
    public int CursorY { get; private set; }
    public int CursorZ { get; private set; }
    public EditorBrush Brush { get; private set; } = EditorBrush.Obstacle;
    public BoxType BoxType { get; private set; } = BoxType.Medium;
    public int PortalTarget { get; private set; } = 1;
    public Level Working => _working;
    public string Status { get; private set; } = "";

    /// <summary>Disparado quando as dimensões do grid mudam (a câmera precisa reenquadrar).</summary>
    public event Action GridChanged;

    public LevelEditor(LevelManager levels)
    {
        _levels = levels;
    }

    /// <summary>
    /// Entra no editor sobre a sessão dada: clona a receita ativa e recarrega o nível pristino
    /// (o editor mostra o design, não o estado de jogo). Recebe o teclado atual pra a detecção
    /// de borda não disparar a brush no mesmo frame do Tab.
    /// </summary>
    public void Enter(GameWorld session, KeyboardState keyboard)
    {
        _session = session;
        _working = session.CurrentLevel?.Clone() ?? BlankLevel(-1);
        _prev = keyboard;

        CursorX = _working.Width / 2;
        CursorZ = _working.Depth / 2;
        CursorY = Math.Min(1, _working.Height - 1);
        Status = $"Editando \"{_working.Name}\"";

        Rebuild();
        GridChanged?.Invoke();
    }

    /// <summary>
    /// Sai do editor: a receita editada vira o novo <see cref="GameWorld.CurrentLevel"/> e é
    /// recarregada, então o jogo recomeça já a partir do design editado.
    /// </summary>
    public void Exit(GameWorld session)
    {
        session.CurrentLevel = _working;
        _levels.LoadLevel(session, _working);
        _working = null;
        _session = null;
    }

    public void Update(GameWorld session, KeyboardState keyboard)
    {
        _session = session;
        bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);

        if (shift)
            HandleResize(keyboard);
        else
            HandleCursor(keyboard);

        HandleBrushSelection(keyboard);
        HandlePlacement(keyboard);
        HandleFiles(keyboard);

        _prev = keyboard;
    }

    // ----- Cursor -----

    private void HandleCursor(KeyboardState k)
    {
        // Mesmo esquema do movimento no jogo: A/D = X, W/S = Z, Q/E = altura (Y).
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
        if (Pressed(k, Keys.D1)) Brush = EditorBrush.Obstacle;
        if (Pressed(k, Keys.D2))
        {
            // Repetir a tecla da caixa cicla o tipo; a primeira só seleciona a brush.
            if (Brush == EditorBrush.Box)
                BoxType = (BoxType)(((int)BoxType + 1) % Enum.GetValues<BoxType>().Length);
            Brush = EditorBrush.Box;
        }
        if (Pressed(k, Keys.D3)) Brush = EditorBrush.Objective;
        if (Pressed(k, Keys.D4)) Brush = EditorBrush.Portal;
        if (Pressed(k, Keys.D5)) Brush = EditorBrush.Player;
        if (Pressed(k, Keys.D0)) Brush = EditorBrush.Eraser;

        if (Pressed(k, Keys.OemOpenBrackets)) PortalTarget = Math.Max(0, PortalTarget - 1);
        if (Pressed(k, Keys.OemCloseBrackets)) PortalTarget++;
    }

    // ----- Colocar / apagar -----

    private void HandlePlacement(KeyboardState k)
    {
        if (Pressed(k, Keys.Space))
        {
            Apply();
            Rebuild();
        }
        else if (Pressed(k, Keys.Delete) || Pressed(k, Keys.Back))
        {
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
                _working.ObstacleSpawns.Add((x, y, z));
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
    }

    private void RemoveMarkersAt(int x, int y, int z)
    {
        _working.ObjectiveSpawns.RemoveAll(c => c.X == x && c.Y == y && c.Z == z);
        _working.PortalSpawns.RemoveAll(c => c.X == x && c.Y == y && c.Z == z);
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
            Status = $"Grid {_working.Width}x{_working.Height}x{_working.Depth}";
            GridChanged?.Invoke();
        }
    }

    private bool Resize(int w, int h, int d)
    {
        w = Math.Clamp(w, MinSize, MaxSize);
        h = Math.Clamp(h, MinSize, MaxSize);
        d = Math.Clamp(d, MinSize, MaxSize);
        if (w == _working.Width && h == _working.Height && d == _working.Depth)
            return false;
        _working.Width = w;
        _working.Height = h;
        _working.Depth = d;
        return true;
    }

    /// <summary>Remove spawns que caíram fora dos novos limites depois de encolher o grid.</summary>
    private void DropOutOfBounds()
    {
        bool Out(int x, int y, int z) =>
            x < 0 || x >= _working.Width || y < 0 || y >= _working.Height || z < 0 || z >= _working.Depth;

        _working.ObstacleSpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
        _working.BoxSpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
        _working.PlayerSpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
        _working.EnemySpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
        _working.ObjectiveSpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
        _working.PortalSpawns.RemoveAll(c => Out(c.X, c.Y, c.Z));
    }

    // ----- Arquivos -----

    private void HandleFiles(KeyboardState k)
    {
        if (Pressed(k, Keys.F5)) SaveToDisk();
        if (Pressed(k, Keys.F9)) LoadFromDisk();
        if (Pressed(k, Keys.N)) NewBlank();
    }

    private string PathFor(Level level)
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "CustomLevels");
        string file = level.Id >= 0 ? $"level_{level.Id}.json" : "custom.json";
        return Path.Combine(dir, file);
    }

    private void SaveToDisk()
    {
        var path = PathFor(_working);
        LevelSerializer.Save(_working, path);
        Status = $"Salvo: {path}";
        Log.Information("Editor: nivel salvo em {Path}", path);
    }

    private void LoadFromDisk()
    {
        var path = PathFor(_working);
        if (!File.Exists(path))
        {
            Status = $"Sem arquivo: {path}";
            return;
        }

        _working = LevelSerializer.Load(path);
        ClampCursor();
        Rebuild();
        Status = $"Carregado: {path}";
        GridChanged?.Invoke();
        Log.Information("Editor: nivel carregado de {Path}", path);
    }

    private void NewBlank()
    {
        int id = _working.Id;
        _working = BlankLevel(id);
        CursorX = _working.Width / 2;
        CursorZ = _working.Depth / 2;
        CursorY = 1;
        Rebuild();
        Status = "Novo nivel em branco";
        GridChanged?.Invoke();
    }

    private static Level BlankLevel(int id)
    {
        var level = new Level { Id = id, Name = "Custom", Width = 9, Height = 4, Depth = 9 };
        level.FillFloor();
        level.PlayerSpawns.Add((4, 1, 4));
        return level;
    }

    private void Rebuild() => _levels.LoadLevel(_session, _working);

    private bool Pressed(KeyboardState current, Keys key)
        => current.IsKeyDown(key) && _prev.IsKeyUp(key);
}
