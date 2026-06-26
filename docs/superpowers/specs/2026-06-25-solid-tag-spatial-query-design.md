# Design — Tag `Solid` + `SpatialQuery`

Data: 2026-06-25

## Objetivo

Refactor sem mudança de comportamento, seguindo ECS mais à risca, para:

- **DRY (#1):** centralizar o lookup espacial "achar a entity na célula (x,y,z)", hoje
  reimplementado em 5 lugares (`FindBoxAt`, `FindOccupantAt`, `FindPlayerCell`,
  `PlayerOnObjective`, lookup de portal inline).
- **Correção (#2):** unificar o conceito "essa peça ocupa o grid?", hoje duplicado e
  **divergente** entre `MovementSystem.Occupies` (não conta `Enemy`) e
  `History.Occupies` (conta). Duas fontes de verdade = bug latente.

Fora de escopo: `MoveEntity` centralizado (#3) e quebrar `MovementSystem` em
Input/Gravity systems (#4).

## Abordagem: ECS-puro com tag `Solid`

`Solid` passa a ser a **única** fonte de verdade sobre "ocupa uma célula". Por isso
`Box.Broken` é **removido** — manter os dois seria reintroduzir a dupla-verdade que o
refactor existe pra eliminar.

## Mudanças

### 1. Component `Solid` (tag vazio)
Em `ECS/Components/GameComponents.cs`. Adicionado no spawn (`LevelManager.LoadLevel`) a:
Player, Box, Enemy, Obstacle. **Não** a Objective nem LevelPortal (não ocupam grid).

### 2. Remover `Box.Broken`
- `struct Box` passa a ter só `Type`.
- Quebrar frágil (`MovementSystem.BreakBox`): `Grid.SetOccupied(false)` + `World.Remove<Solid>(e)`.
  A entity persiste (pro undo), mas não ocupa nem é desenhada.
- `RenderSystem.DrawEntities` (caixas): query `WithAll<Box, RenderPosition, Solid>` — as
  quebradas (sem `Solid`) somem naturalmente, sem checagem de flag.

### 3. `SpatialQuery` (novo, em `Core/`)
Exposto como `GameWorld.Spatial` (construído no ctor com o `World`). API:

- `Entity? Occupant(int x, int y, int z)` — primeira entity `WithAll<Solid, GridPosition>`
  na célula. Substitui `FindBoxAt` e `FindOccupantAt`.
- `Entity? CellWith<T>(int x, int y, int z) where T : struct` — primeira entity com o tag
  `T` na célula. Substitui o lookup de Objective e de LevelPortal.
- `Entity? First<T>() where T : struct` — primeira entity com `T`. Para o player único.

### 4. "Ocupa?" unificado
Todo `Occupies(e)` vira `World.Has<Solid>(e)`:
- Remove `MovementSystem.Occupies`; `ApplyGravity` usa `Has<Solid>`.
- Remove `History.Occupies`; usa `Has<Solid>`.
- Comportamento idêntico e correto: ambos passam a contar Enemy (corrige a divergência).
  Não muda resultado em prática — Enemy nunca entra no `record` do Movement e bloqueia no
  undo de qualquer forma — mas elimina a fonte dupla.

### 5. `EntityState`: `Box? BoxState` → `bool WasSolid`
`Box` não tem mais estado mutável (só `Type`, imutável), então não há mais o que restaurar
via `BoxState`. O que o undo precisa reverter é a **presença de `Solid`** (frágil que
quebrou volta inteira; restart desfeito volta ao estado quebrado).

`History.Undo`, passo 1 (antes de mexer em posição): para cada `state`, reconcilia `Solid`
ao valor `WasSolid` — adiciona se faltava, remove se sobrava. Passo 2 (posição) usa
`Has<Solid>` como `Occupies`.

Sites que constroem `EntityState` passam `WasSolid = World.Has<Solid>(e)` no momento do
registro.

### 6. Mudança estrutural fora de query (Arch)
`Remove<Solid>`/`Add<Solid>` são mudanças estruturais (trocam o arquétipo da entity) —
proibidas durante a iteração de um `World.Query`. Dois ajustes:

- **`MovementSystem.Update`:** hoje roda `TryMovePlayer` *dentro* de `World.Query(players)`.
  Passa a pegar o player único via `Spatial.First<Player>()` e roda `TryMovePlayer` **fora**
  de qualquer query. (A quebra de frágil acontece aqui dentro.)
- **`LevelManager.Restart`:** re-adicionar `Solid` em caixas que estavam quebradas é feito
  **após** a query de reposição, sobre uma lista coletada durante ela.

`History.Undo` já opera sobre uma `List` (não query), então `Add/Remove<Solid>` ali é seguro.

## Lista de arquivos tocados

- `ECS/Components/GameComponents.cs` — add `Solid`; remove `Box.Broken`.
- `Core/SpatialQuery.cs` — **novo**.
- `Core/GameWorld.cs` — expõe `Spatial`.
- `Core/History.cs` — `EntityState.WasSolid`; `Undo` reconcilia `Solid`; usa `Occupant`/`Has<Solid>`.
- `ECS/Systems/MovementSystem.cs` — `First<Player>` fora da query; `Occupant`; `BreakBox` via `Remove<Solid>`; remove `Occupies`/`FindBoxAt`.
- `ECS/Systems/RenderSystem.cs` — caixas via `WithAll<Box, RenderPosition, Solid>`.
- `Levels/LevelManager.cs` — `Solid` no spawn; `Restart` re-adiciona `Solid` (deferido); snapshot com `WasSolid`.
- `Game1.cs` — `FindPlayerCell`/`PlayerOnObjective`/`TryEnterPortal` via `Spatial`.

## Verificação

Sem projeto de testes (decisão do usuário; mantém o CLAUDE.md). `dotnet build` + rodar o
jogo, exercitando manualmente: empurrar caixas (peso/força), quebrar frágil, gravidade
(buracos/chão-morte), undo (Z), restart (R), suspender (T) e portais/objetivo.

## Notas

- `TryEnterPortal` original casava portal só por X/Z (ignorava Y); `CellWith<LevelPortal>`
  casa X/Y/Z. Como player e portal compartilham Y, o resultado é o mesmo — e mais correto.
