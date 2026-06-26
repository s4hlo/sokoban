using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sokoban3D.ECS.Components;

namespace Sokoban3D.Levels;

/// <summary>
/// Salva e carrega um <see cref="Level"/> como JSON legível. A leitura usa System.Text.Json
/// com um DTO dedicado; a escrita é feita à mão pra deixar o arquivo bonito: cada célula numa
/// linha só (<c>[x, y, z]</c> pra marcadores/obstáculos, <c>[x, y, z, "Tipo"]</c> pra caixas,
/// <c>[x, y, z, alvo, concluido]</c> pra portais).
/// </summary>
public static class LevelSerializer
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        Converters =
        {
            new CellConverter(),
            new BoxCellConverter(),
            new PortalCellConverter(),
            new PlateCellConverter(),
            new ToggleCellConverter(),
        },
    };

    // ----- Escrita (à mão, pra o layout ficar limpo) -----

    public static void Save(Level l, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"Name\": {JsonSerializer.Serialize(l.Name)},\n");
        sb.Append($"  \"Id\": {l.Id},\n");
        sb.Append($"  \"Width\": {l.Width}, \"Height\": {l.Height}, \"Depth\": {l.Depth},\n");

        AppendArray(sb, "Players", Format(l.PlayerSpawns), last: false);
        AppendArray(sb, "Boxes", FormatBoxes(l.BoxSpawns), last: false);
        AppendArray(sb, "Objectives", Format(l.ObjectiveSpawns), last: false);
        AppendArray(sb, "Enemies", Format(l.EnemySpawns), last: false);
        AppendArray(sb, "Obstacles", Format(l.ObstacleSpawns), last: false);
        AppendArray(sb, "Portals", FormatPortals(l.PortalSpawns), last: false);
        AppendArray(sb, "Plates", FormatPlates(l.PlateSpawns), last: false);
        AppendArray(sb, "Toggles", FormatToggles(l.ToggleSpawns), last: true);

        sb.Append("}\n");
        File.WriteAllText(path, sb.ToString());
    }

    private static List<string> Format(List<(int X, int Y, int Z)> cells)
    {
        var list = new List<string>(cells.Count);
        foreach (var (x, y, z) in cells)
            list.Add($"[{x}, {y}, {z}]");
        return list;
    }

    private static List<string> FormatBoxes(List<(int X, int Y, int Z, BoxType Type)> cells)
    {
        var list = new List<string>(cells.Count);
        foreach (var (x, y, z, t) in cells)
            list.Add($"[{x}, {y}, {z}, \"{t}\"]");
        return list;
    }

    private static List<string> FormatPortals(List<(int X, int Y, int Z, int LevelIndex, bool Completed)> cells)
    {
        var list = new List<string>(cells.Count);
        foreach (var (x, y, z, idx, done) in cells)
            list.Add($"[{x}, {y}, {z}, {idx}, {(done ? "true" : "false")}]");
        return list;
    }

    private static List<string> FormatPlates(List<(int X, int Y, int Z, int Group)> cells)
    {
        var list = new List<string>(cells.Count);
        foreach (var (x, y, z, g) in cells)
            list.Add($"[{x}, {y}, {z}, {g}]");
        return list;
    }

    private static List<string> FormatToggles(List<(int X, int Y, int Z, int Group, bool SolidByDefault)> cells)
    {
        var list = new List<string>(cells.Count);
        foreach (var (x, y, z, g, solid) in cells)
            list.Add($"[{x}, {y}, {z}, {g}, {(solid ? "true" : "false")}]");
        return list;
    }

    /// <summary>Escreve a propriedade-array com um item por linha (ou <c>[]</c> se vazia).</summary>
    private static void AppendArray(StringBuilder sb, string name, List<string> items, bool last)
    {
        sb.Append("  \"").Append(name).Append("\": ");
        if (items.Count == 0)
        {
            sb.Append("[]");
        }
        else
        {
            sb.Append("[\n");
            for (int i = 0; i < items.Count; i++)
                sb.Append("    ").Append(items[i]).Append(i < items.Count - 1 ? ",\n" : "\n");
            sb.Append("  ]");
        }
        sb.Append(last ? "\n" : ",\n");
    }

    // ----- Leitura -----

    public static Level Load(string path)
    {
        var dto = JsonSerializer.Deserialize<LevelDto>(File.ReadAllText(path), ReadOptions);
        return FromDto(dto);
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
        foreach (var p in dto.Plates) level.PlateSpawns.Add((p.X, p.Y, p.Z, p.Group));
        foreach (var t in dto.Toggles) level.ToggleSpawns.Add((t.X, t.Y, t.Z, t.Group, t.SolidByDefault));

        return level;
    }

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
        public List<PlateCell> Plates { get; set; } = new();
        public List<ToggleCell> Toggles { get; set; } = new();
    }

    private record struct Cell(int X, int Y, int Z);
    private record struct BoxCell(int X, int Y, int Z, BoxType Type);
    private record struct PortalCell(int X, int Y, int Z, int LevelIndex, bool Completed);
    private record struct PlateCell(int X, int Y, int Z, int Group);
    private record struct ToggleCell(int X, int Y, int Z, int Group, bool SolidByDefault);

    private static void ExpectArray(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Esperava um array [x, y, z, ...] pra uma célula.");
    }

    private sealed class CellConverter : JsonConverter<Cell>
    {
        public override Cell Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            ExpectArray(ref reader);
            reader.Read(); int x = reader.GetInt32();
            reader.Read(); int y = reader.GetInt32();
            reader.Read(); int z = reader.GetInt32();
            reader.Read(); // EndArray
            return new Cell(x, y, z);
        }

        public override void Write(Utf8JsonWriter writer, Cell v, JsonSerializerOptions options)
            => writer.WriteRawValue($"[{v.X}, {v.Y}, {v.Z}]");
    }

    private sealed class BoxCellConverter : JsonConverter<BoxCell>
    {
        public override BoxCell Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            ExpectArray(ref reader);
            reader.Read(); int x = reader.GetInt32();
            reader.Read(); int y = reader.GetInt32();
            reader.Read(); int z = reader.GetInt32();
            reader.Read(); var t = Enum.Parse<BoxType>(reader.GetString()!, ignoreCase: true);
            reader.Read(); // EndArray
            return new BoxCell(x, y, z, t);
        }

        public override void Write(Utf8JsonWriter writer, BoxCell v, JsonSerializerOptions options)
            => writer.WriteRawValue($"[{v.X}, {v.Y}, {v.Z}, \"{v.Type}\"]");
    }

    private sealed class PortalCellConverter : JsonConverter<PortalCell>
    {
        public override PortalCell Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            ExpectArray(ref reader);
            reader.Read(); int x = reader.GetInt32();
            reader.Read(); int y = reader.GetInt32();
            reader.Read(); int z = reader.GetInt32();
            reader.Read(); int idx = reader.GetInt32();
            reader.Read(); bool done = reader.GetBoolean();
            reader.Read(); // EndArray
            return new PortalCell(x, y, z, idx, done);
        }

        public override void Write(Utf8JsonWriter writer, PortalCell v, JsonSerializerOptions options)
            => writer.WriteRawValue($"[{v.X}, {v.Y}, {v.Z}, {v.LevelIndex}, {(v.Completed ? "true" : "false")}]");
    }

    private sealed class PlateCellConverter : JsonConverter<PlateCell>
    {
        public override PlateCell Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            ExpectArray(ref reader);
            reader.Read(); int x = reader.GetInt32();
            reader.Read(); int y = reader.GetInt32();
            reader.Read(); int z = reader.GetInt32();
            reader.Read(); int g = reader.GetInt32();
            reader.Read(); // EndArray
            return new PlateCell(x, y, z, g);
        }

        public override void Write(Utf8JsonWriter writer, PlateCell v, JsonSerializerOptions options)
            => writer.WriteRawValue($"[{v.X}, {v.Y}, {v.Z}, {v.Group}]");
    }

    private sealed class ToggleCellConverter : JsonConverter<ToggleCell>
    {
        public override ToggleCell Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            ExpectArray(ref reader);
            reader.Read(); int x = reader.GetInt32();
            reader.Read(); int y = reader.GetInt32();
            reader.Read(); int z = reader.GetInt32();
            reader.Read(); int g = reader.GetInt32();
            reader.Read(); bool solid = reader.GetBoolean();
            reader.Read(); // EndArray
            return new ToggleCell(x, y, z, g, solid);
        }

        public override void Write(Utf8JsonWriter writer, ToggleCell v, JsonSerializerOptions options)
            => writer.WriteRawValue($"[{v.X}, {v.Y}, {v.Z}, {v.Group}, {(v.SolidByDefault ? "true" : "false")}]");
    }
}
