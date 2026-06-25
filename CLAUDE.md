# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

3D Sokoban built on **MonoGame** (DesktopGL) targeting **.NET 9**, using the **Arch** archetype-based ECS. The game logic operates on a 3D integer grid (X horizontal, Y vertical, Z depth). The project is in an early scaffolding stage: movement and level loading exist, but there is no rendering of game entities yet (`Draw` only clears the screen).

Source comments and `<summary>` docs are written in **Portuguese**; code identifiers are in English. Match this convention when editing.

## Commands

All commands run from the `Sokoban3D/` directory (it holds the `.csproj`; there is no `.sln`).

```bash
dotnet build          # build (also compiles Content via MonoGame.Content.Builder.Task)
dotnet run            # build and launch the game
dotnet tool restore   # restore the mgcb content-builder tools (.config/dotnet-tools.json) — needed before first build
```

There is no test project or linter configured. Debugging is also available via the VS Code launch config "C#: Sokoban3D Debug".

In-game: arrow keys move on the X/Z plane, `W`/`S` move on the Y axis, `Esc` (or gamepad Back) quits.

## Architecture

The flow is `Program.cs` → `Game1` (the MonoGame `Game` loop) → owns three collaborators wired in `Initialize`/`LoadContent`:

- **`GameWorld`** (`Core/`) — composition root holding the Arch `World` (entity storage) and the `GridManager`. Owns disposal of the ECS world.
- **`GridManager`** (`Grid/`) — a `bool[,,]` occupancy map plus bounds checks. Key contract: `IsOccupied` returns `true` for out-of-bounds cells, so callers can treat the grid edge as a wall. This is the single source of truth for spatial collision; the ECS does not track occupancy itself.
- **`LevelManager`** (`Levels/`) — a `Level` is a plain data bag of spawn tuples (players, boxes, objectives, enemies). `LoadLevel` clears the grid, then creates Arch entities and marks grid cells occupied. **Objectives intentionally do not occupy grid cells** (boxes must be able to move onto them).

### ECS conventions (Arch)

- **Components** (`ECS/Components/`) are plain `struct`s: `GridPosition` (the position), and tag-with-data structs `Player`, `Box`, `Objective`, `Enemy`.
- **Systems** (`ECS/Systems/`) are hand-instantiated classes (not an Arch `SystemGroup`) called explicitly from `Game1.Update`. `MovementSystem` queries `WithAll<Player, GridPosition>()`, validates the target cell against `GridManager`, and on a valid move updates **both** the grid occupancy and the entity's `GridPosition` via `World.Set`. Any new mover must keep grid occupancy and component position in sync the same way.

When adding a system, instantiate it in `Game1.Initialize` and invoke it from `Game1.Update`.

## Content pipeline

`Content/Content.mgcb` is the MonoGame content project, built automatically during `dotnet build`. Edit it with the `mgcb-editor` tool (restored via `dotnet tool restore`).
