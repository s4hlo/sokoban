using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Levels;

/// <summary>
/// Salva e carrega um <see cref="Level"/> como JSON legível. Usa um DTO dedicado (em vez de
/// serializar os ValueTuples direto) pra o arquivo ficar com nomes claros e estável a mudanças
/// internas das listas de spawn.
/// </summary>
public static class LevelSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Save(Level level, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(ToDto(level), Options));
    }

    public static Level Load(string path)
    {
        var dto = JsonSerializer.Deserialize<LevelDto>(File.ReadAllText(path), Options);
        return FromDto(dto);
    }

    private static LevelDto ToDto(Level l)
    {
        var dto = new LevelDto
        {
            Name = l.Name,
            Id = l.Id,
            Width = l.Width,
            Height = l.Height,
            Depth = l.Depth,
        };

        foreach (var (x, y, z) in l.PlayerSpawns) dto.Players.Add(new(x, y, z));
        foreach (var (x, y, z, t) in l.BoxSpawns) dto.Boxes.Add(new(x, y, z, t));
        foreach (var (x, y, z) in l.ObjectiveSpawns) dto.Objectives.Add(new(x, y, z));
        foreach (var (x, y, z) in l.EnemySpawns) dto.Enemies.Add(new(x, y, z));
        foreach (var (x, y, z) in l.ObstacleSpawns) dto.Obstacles.Add(new(x, y, z));
        foreach (var (x, y, z, idx, done) in l.PortalSpawns) dto.Portals.Add(new(x, y, z, idx, done));

        return dto;
    }

    private static Level FromDto(LevelDto dto)
    {
        var level = new Level
        {
            Name = dto.Name,
            Id = dto.Id,
            Width = dto.Width,
            Height = dto.Height,
            Depth = dto.Depth,
        };

        foreach (var c in dto.Players) level.PlayerSpawns.Add((c.X, c.Y, c.Z));
        foreach (var c in dto.Boxes) level.BoxSpawns.Add((c.X, c.Y, c.Z, c.Type));
        foreach (var c in dto.Objectives) level.ObjectiveSpawns.Add((c.X, c.Y, c.Z));
        foreach (var c in dto.Enemies) level.EnemySpawns.Add((c.X, c.Y, c.Z));
        foreach (var c in dto.Obstacles) level.ObstacleSpawns.Add((c.X, c.Y, c.Z));
        foreach (var p in dto.Portals) level.PortalSpawns.Add((p.X, p.Y, p.Z, p.LevelIndex, p.Completed));

        return level;
    }

    // ----- DTO de serialização -----

    private sealed class LevelDto
    {
        public string Name { get; set; } = "Custom";
        public int Id { get; set; } = -1;
        public int Width { get; set; }
        public int Height { get; set; }
        public int Depth { get; set; }
        public List<Cell> Players { get; set; } = new();
        public List<BoxCell> Boxes { get; set; } = new();
        public List<Cell> Objectives { get; set; } = new();
        public List<Cell> Enemies { get; set; } = new();
        public List<Cell> Obstacles { get; set; } = new();
        public List<PortalCell> Portals { get; set; } = new();
    }

    private record struct Cell(int X, int Y, int Z);
    private record struct BoxCell(int X, int Y, int Z, BoxType Type);
    private record struct PortalCell(int X, int Y, int Z, int LevelIndex, bool Completed);
}
