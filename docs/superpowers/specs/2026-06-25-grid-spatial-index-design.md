# Grid como índice espacial de entidades

**Data:** 2026-06-25
**Status:** Aprovado para planejamento
**Escopo:** Refactor comportamento-preservado da ocupação do grid (item 1 da análise DRY/SOLID/ECS).

## Problema

A ocupação de cada célula vive hoje em **dois lugares que precisam ser sincronizados na mão**:

1. O `bool[,,]` do `GridManager`.
2. O conjunto `Solid + GridPosition` no ECS.

Toda mutação repete o mesmo tridente — "libera célula antiga / ocupa célula nova / `World.Set` da posição" — em 6+ lugares:

- `MovementSystem.TryMovePlayer` (player) — `Sokoban3D/ECS/Systems/MovementSystem.cs:73-75`
- `MovementSystem.TryClear` (empurrão) — `:160-162`
- `MovementSystem.ApplyGravity` (gravidade) — `:116-118`
- `History.Undo` — `Sokoban3D/Core/History.cs:81-95`
- `History.TryClearForUndo` — `:133-135`
- `LevelManager.LoadLevel` / `Restart` — 5x `Create(...)` + `SetOccupied`

Esquecer um lado em qualquer uma dessas cópias deixa o grid mentindo. Além disso,
`SpatialQuery.Occupant` faz um **scan linear de todos os `Solid` por chamada**, e é
invocado recursivamente dentro de `TryClear`/`TryClearForUndo`: o `bool[,,]` diz *se* a
célula tem algo, mas pra saber *quem* varre o mundo inteiro.

## Objetivo

- Transformar o grid no índice espacial de verdade (célula → entity), eliminando o scan O(n).
- Centralizar a invariante "célula + `GridPosition` andam juntas" numa **única** API de mutação.
- **Não mudar nenhum comportamento de jogo.** Os 4 níveis existentes devem se comportar
  identicamente (empurrão por peso, gravidade, chão-morte, frágil quebra, permanente/verde,
  undo, restart, portais).

Fora de escopo: regras de jogo, win-condition, novos tipos de caixa, projeto de teste novo.

## Design

### 1. `GridManager`: `bool[,,]` → `Entity?[,,]`

Continua **puro**: depende só de `Arch.Core.Entity` (struct), sem referência a `World`.
Vira o índice espacial:

| Antes | Depois |
|---|---|
| `bool[,,] _occupied` | `Entity?[,,] _occupant` |
| `IsOccupied(x,y,z)` lê o `bool` | `IsOccupied(x,y,z)` = `Occupant(x,y,z) != null` |
| — | **`Occupant(x,y,z): Entity?`** — O(1), lê o array direto |
| `SetOccupied(x,y,z,true)` | `Place(x,y,z, Entity e)` |
| `SetOccupied(x,y,z,false)` | `Vacate(x,y,z)` |

Contratos preservados:
- `IsValid(x,y,z)` — inalterado.
- `IsOccupied` continua retornando `true` para fora-dos-limites (a borda é parede).
- `Resize`, `Clear`, `Width`/`Height`/`Depth` — inalterados (`Clear` zera o array para `null`).

Ganho estrutural: cada célula guarda **uma** entity, então "um `Solid` por célula" passa a
ser garantido pela estrutura, não por convenção. `Place` numa célula já ocupada por outra
entity é um bug do chamador — ver "Asserções".

### 2. Serviço de movimento na `GameWorld`

`GameWorld` já agrega `World` + `Grid`, então é o dono natural da **única** API que mexe em
célula + `GridPosition` ao mesmo tempo. Três métodos, nível-entity (leem a `GridPosition`
atual do componente):

- **`Move(Entity e, GridPosition to)`** — `Vacate` da célula atual → `World.Set(e, to)` →
  `Place` na célula nova. Substitui todos os tridentes.
- **`Occupy(Entity e)`** — `Place` na célula atual da entity. Usado no spawn e ao
  re-solidificar (undo de uma frágil quebrada; restart).
- **`Vacate(Entity e)`** — `Vacate` da célula atual sem mover. Usado ao quebrar uma frágil.

`Move` **assume jogada legal**: quem chama já validou com `IsValid`/`IsOccupied`/`TryClear`.
Mantém o padrão atual (valida → muta).

### 3. Reescrita dos call-sites

- `MovementSystem`: os 3 tridentes (player, empurrão, gravidade) → `session.Move(e, to)`.
- Quebrar frágil (`MovementSystem.BreakBox`): `session.Vacate(e)` + `World.Remove<Solid>(e)`.
- `History.Undo`, re-solidificar: `World.Add<Solid>(e)` + `Occupy(e)`.
- `History.Undo`, volta de posição: **não** colapsa num único `Move`, porque há um
  "clear do alvo" intercalado — a estrutura é `Vacate(e)` → `TryClearForUndo(alvo)` →
  `World.Set(e, alvo)` + `Occupy(e)`. Os passos atômicos saem dos helpers da `GameWorld`/`Grid`,
  mas a sequência (liberar atual → limpar alvo → ocupar alvo) é preservada como hoje.
- `History.TryClearForUndo`: empurrão em cadeia da verde → `session.Move(e, à-frente)`;
  lookup do ocupante → `Grid.Occupant`.
- `LevelManager.LoadLevel`: cada `Create(...)` + `SetOccupied(true)` → `Create(...)` + `Occupy(e)`.
- `LevelManager.Restart`: re-marcação de obstáculos e peças → `Occupy(e)`; re-solidificar → `Occupy(e)`.
- **`SpatialQuery.Occupant` é deletado.** Chamadores passam a usar `Grid.Occupant`.
  `SpatialQuery.CellWith<T>` e `First<T>` (objetivo, portal, player — peças **fora** do grid)
  **ficam**: são raros (uma vez por frame/input) e não dependem da ocupação.

### Asserções

`GridManager.Place(x,y,z,e)` ganha um `Debug.Assert` de que a célula está vazia ou já contém
`e` (pega dessincronização em debug, sem custo em release). `Move`/`Vacate`/`Occupy` operam
sobre a `GridPosition` lida do componente — se ela divergir do array, o assert acusa.

## Verificação

Refactor comportamento-preservado, sem projeto de teste no repo. Verificação:

1. `dotnet build` (a partir de `Sokoban3D/`) — compila limpo.
2. `dotnet run` e exercitar manualmente cada invariante de jogo:
   - empurrão por peso (leve/média/pesada, fila de 2 médias, pesada sozinha);
   - frágil quebra ao ser prensada contra algo imóvel;
   - permanente/verde move mas o undo não a reverte (só o R);
   - gravidade: peça empurrada pra cima de buraco cai; pisar no buraco → chão-morte → congela;
   - undo (Z) reverte movimento + queda numa ação só; restart (R); undo do restart;
   - entrar/concluir/suspender portal preserva ou reseta o estado conforme esperado.

Critério de aceite: comportamento idêntico ao de antes do refactor em todos os pontos acima.

## Riscos

- **`Move` lendo a posição "atual" durante cadeias** (gravidade, empurrão múltiplo): cada
  `Move` lê a `GridPosition` do componente, que o próprio `Move` mantém em dia — então
  cadeias de `Move` sequenciais ficam coerentes. É o mesmo pressuposto do código atual.
- **Ordem no undo**: `History.Undo` já restaura `Solid` antes da posição e move de trás pra
  frente. A tradução pra `Move`/`Occupy` preserva essa ordem — não a altera.
