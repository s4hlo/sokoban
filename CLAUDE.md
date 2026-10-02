# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

3D Sokoban on a 3D integer grid (X horizontal, Y vertical/gravity, Z depth), built on **MonoGame** (DesktopGL) targeting **.NET 9**, using the **Arch** archetype ECS. Beyond core play it now has full cube rendering, an in-game level editor, a headless puzzle solver + test suite, per-level analytics, and a tree of levels navigated by portals.

Source comments and `<summary>` docs are written in **Portuguese**; code identifiers are in English. Match this convention when editing.

## Commands

There is no `.sln`. The game project lives in `Sokoban3D/` (holds the `.csproj`); tests in `Sokoban3D.Tests/`.

```bash
dotnet tool restore                          # once: restores mgcb content-builder tools (.config/dotnet-tools.json)

# from Sokoban3D/
dotnet build                                 # build (compiles Content via MonoGame.Content.Builder.Task)
dotnet run                                    # build and launch the game

# from Sokoban3D.Tests/
dotnet test                                   # run the full suite (headless, same engine as the game)
dotnet test --filter MapWithObjectiveIsSolvable          # one test class/method
dotnet test --filter "MapWithObjectiveIsSolvable(id: 7)" # one theory case (a single level id)
```

> Do NOT run `dotnet build`/`dotnet run` autonomously — the user validates the running game themselves. Running `dotnet test` is fine.

VS Code debug is available via the "C#: Sokoban3D Debug" launch config. Runtime logs go to `logs/sokoban-YYYYMMDD.log` (Serilog; also captures fatal crashes from `Program.cs`).

## The golden rule: one engine, driven two ways

`MovementSystem.Step(GameWorld, dx, dz)` is the single canonical definition of a game turn — push, portal teleport, gravity, pressure plates, history commit, cell effects. **Both** the game's keyboard input (`MovementSystem.Update`, a thin edge-detection wrapper) **and** the solver/tests (headless, no `GraphicsDevice`) call the same `Step`/`Undo`. Never reimplement a rule in the solver or tests — a second copy diverges (portals, magnetic rotation, gravity order) and starts "proving" a game that doesn't exist. Animation scheduling embedded in `Step` only writes components; headless callers ignore them.

Dependency direction is one-way: **tools → engine, never engine → tools.** The solver core (`Solver/PuzzleSolver`, `SolverState`, `SolverSim`, `SolverReach`) is 100% free of MonoGame; only `SolverTool`/`SolverRenderer` (in-game playback) touch it.

## Architecture

`Program.cs` → `Game1` (MonoGame loop). `Game1.Update` is a mode dispatcher: normal play, level editor (Tab), solver playback (P/C), and a modal level browser (M) each capture input exclusively.

### Sessions and the level tree

- **`GameWorld`** (`Core/`) is one level session: an Arch `World` + `GridManager` + `History` + `SpatialQuery`. Every level — including the root — is the same abstraction; there is no special "hub".
- **`LevelNavigator`** (`Core/`) owns navigation as a **stack of sessions**. Entering a `LevelPortal` (Enter) pushes a child; reaching the objective (`CompleteActive`) pops and **discards** it (next visit rebuilds fresh); suspending (T, `SuspendActive`) pops but **keeps** it cached (re-entry restores exact state). A parent session stays intact on the stack while you're in a child — no serialization needed.
- **`LevelManager`** (`Levels/`) is a stateless service that spawns a `Level` recipe into a session. `Restart` (R) repositions existing entities to their `SpawnPosition` and records the move (so undo can revert the restart); `FullReset` (F) reloads from scratch and clears history.

### Grid occupancy invariant

`GridManager` is a `bool[,,]`/occupant map and the single source of truth for spatial collision — the ECS does not track occupancy independently. `IsOccupied` returns **true for out-of-bounds** so callers treat the grid edge as a wall. **`GameWorld.Move`/`Occupy`/`Vacate` are the only legal mutators of position+occupancy together** — they keep `GridPosition` and the grid in sync and are footprint-aware (a `BigBox` occupies two cells). Any new mover must go through them.

### ECS conventions (Arch)

- **Components** (`ECS/Components/`) are plain `struct`s. Two tag families matter:
  - **`Solid`** = "occupies the grid". `World.Has<Solid>(e)` is the answer to "does this block movement?". A broken fragile box loses `Solid` but the entity persists (so undo can re-solidify it).
  - **`CellMarker`** = occupies a *logical* cell but not the grid — you can stand on it: `Objective`, `LevelPortal`, `PressurePlate`, `TimelessBase`, `Rail`.
- **Systems** (`ECS/Systems/`) are hand-instantiated (not an Arch `SystemGroup`) and called explicitly from `Game1`. Structural changes (Add/Remove component, Destroy) are illegal mid-`World.Query` — collect entities first, mutate after (see `BreakBox`, `Restart`).
- A turn's deterministic order lives in `MovementSystem.SettleAndCommit`: gravity (`Gravity.Settle`, Y is always derived, never chosen) → pressure plates (`PressurePlateSystem.Resolve`) → fragile departures → history commit → `TimelessBase` forget → death-floor check (`y==0` ⇒ `PlayerFell`, frozen until Z/R/T).

### Derived state is never stored or historized

Toggle-block solidity is derived from plate occupancy every frame; magnetic-body adjacency is derived from position (nothing stored). Neither goes into `History` — undo restores positions and `PressurePlateSystem.Resolve` re-derives the rest. The `Permanent` (green) box is excluded from snapshots entirely (only R reverts it), which makes undo asymmetric around it.

### Mechanics helpers (`Core/`)

Rule fragments `Step` composes, each a small pure(-ish) helper so push/gravity/rotation never disagree: `Gravity`, `Magnetism` (magnetic box ⇒ player becomes a rigid tank body; `Facing` becomes part of search state), `Stickiness`, `Rails` (cargo enters/exits only through a rail's ends), `Restraints`, `Fragility`, `Collectibles`. Box types and weights live in `GameComponents.cs` (`BoxType`/`BoxRules`).

### Solver & tests

`PuzzleSolver.Solve` picks a tier: **macro** (default — compresses inert walking into macro-actions, best-first by a plate heuristic), **BFS by steps** (when a magnetic box makes walking non-inert), or **IDDFS with an Undo action** (levels with `TimelessBase`/`Permanent`, where history stacks enter the state). `Sokoban3D.Tests` runs 100% headless on the real engine: `SolvabilityOracleTests` proves every `Maps/*.json` with an objective is solvable — either by search, or by replaying a certificate `Sokoban3D.Tests/Solutions/level_N.moves` (auto-recorded when you beat a level in-game). `EnginePropertyTests` fuzzes engine invariants (grid ≡ Solid entities, undo is exact except through Forget/green, gravity idempotent, restart == spawns).

## Levels & content

- Level recipes are JSON at `Sokoban3D/Maps/level_<id>.json` — the **source of truth**. `LevelRepository` reads/writes them; `LevelSerializer` handles the format; `LevelCatalog` tracks global completion. The in-game editor (Tab) writes back to these files, so editing changes the official map.
- `Content/Content.mgcb` is the MonoGame content project (textures, `Hud` font), built automatically during `dotnet build`. Edit with `mgcb-editor` (restored via `dotnet tool restore`).

## Controls

Movement: WASD / arrows / HJKL on the X/Z plane. `Z` undo, `R` restart, `F` full reset, `T` suspend (exit to parent, preserved), `,`/`.` jump prev/next level, `Enter` dive into portal, `M` level list, `Tab` editor, `P` solver search (two-press confirm; can freeze ~30s), `C` play recorded certificate, `Esc` steps back one context (never quits — only gamepad Back/closing the window exits).

> Convention for **new** shortcuts (60%/TKL-friendly): avoid arrows, PgUp/PgDn/Home/End and the F-row; keep bindings in the alphanumeric block (vertical nav = W/S). Existing arrow bindings predate this.
