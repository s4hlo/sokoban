# Arquitetura — Sokoban3D

Referência aprofundada da arquitetura do jogo. O [CLAUDE.md](CLAUDE.md) é o resumo operacional; este documento explica **por quê** cada peça existe e como elas se encaixam. Convenção do repo: comentários e `<summary>` em **português**, identificadores em **inglês**.

---

## 1. Visão geral

Sokoban 3D num grid inteiro (X horizontal, Y vertical/gravidade, Z profundidade), sobre **MonoGame** (DesktopGL, .NET 9) e a ECS de arquétipos **Arch**. Além do jogo, o projeto carrega quatro subsistemas de ferramenta: um **editor** in-game, um **solver** headless com **suíte de testes**, **telemetria** por nível e uma **árvore de níveis** navegável por portais.

O princípio que organiza tudo é **um engine só, dirigido de dois jeitos**: `MovementSystem.Step` é a definição única de um turno; o teclado do jogo e o solver/testes (headless) chamam o mesmo método. A dependência é sempre **ferramenta → engine, nunca engine → ferramenta**.

```
┌─────────────────────────────────────────────────────────────────┐
│                          Game1 (loop MonoGame)                    │
│   dispatcher de modos: jogo · editor(Tab) · solver(P/C) · lista(M)│
└───────┬───────────────┬───────────────┬──────────────┬───────────┘
        │               │               │              │
   ┌────▼────┐   ┌───────▼──────┐  ┌─────▼─────┐  ┌─────▼──────┐
   │ Navigator│   │  LevelEditor │  │ SolverTool│  │LevelBrowser│   ← ferramentas
   │ (pilha)  │   │   (Editor/)  │  │ (Solver/) │  │  (Levels/) │
   └────┬────┘   └───────┬──────┘  └─────┬─────┘  └────────────┘
        │               │                │
        ▼               ▼                ▼
   ┌───────────────────────────────────────────────┐
   │              GameWorld  (sessão)               │   ← ENGINE
   │   World(Arch) · GridManager · History · Spatial│
   │   MovementSystem.Step / Undo   ← regra única   │
   │   Gravity · Magnetism · Rails · Fragility ...   │
   └───────────────────────────────────────────────┘
```

### Mapa de pastas

| Pasta | Papel |
|---|---|
| `Core/` | Sessão (`GameWorld`), grid-view, histórico, navegação, e os **helpers de mecânica** (`Gravity`, `Magnetism`, `Stickiness`, `Rails`, `Restraints`, `Fragility`, `Collectibles`), câmera, render de cubo. |
| `Grid/` | `GridManager`: índice espacial célula→entity (puro, sem ECS). |
| `ECS/Components/` | `struct`s de dados e tags (`GameComponents.cs`, `GridPosition`, `RenderPosition`). |
| `ECS/Systems/` | `MovementSystem` (o turno), `PressurePlateSystem`, `RenderSystem`, `MoveAnimationSystem`. |
| `Levels/` | Receita `Level`, `LevelManager` (spawn), `LevelCatalog`/`LevelRepository`/`LevelSerializer` (persistência), navegação de UI (`LevelBrowser`). |
| `Editor/` | Editor in-game (dev tool). |
| `Solver/` | Núcleo puro do solver + tool/renderer in-game (dev tool). |
| `Analytics/` | Telemetria por nível (GameAnalytics, opcional). |
| `Maps/` | `level_<id>.json` — **fonte de verdade** dos níveis. |

---

## 2. O loop e os modos (`Game1`)

`Program.cs` monta o Serilog (log em `logs/sokoban-AAAAMMDD.log`, captura crashes) e roda `Game1`. `Game1.Update` é um **dispatcher de modos mutuamente exclusivos** — cada modo captura o input com exclusividade e retorna cedo:

1. **Lista de níveis** (`M`) — modal por cima de tudo (jogando ou editando); W/S navega, Shift+W/S reordena ids em disco, Enter escolhe.
2. **Editor** (`Tab`) — pausa os sistemas de jogo; só o editor processa input.
3. **Solver playback** (`P` busca / `C` certificado) — o input do player fica suspenso; o tool injeta ações no mesmo `Step`.
4. **Jogo normal** — movimento + `Z`/`R`/`F`/`T`/`,`/`.`/`Enter`.

`Esc` nunca encerra o jogo: recua um nível de contexto. Só o Back do gamepad (ou fechar a janela) sai. O campo `Active` é sempre `_navigator.Active` (o topo da pilha de sessões).

`Draw` desenha a cena 3D com a câmera fixa (ou a orbitável do editor), depois os overlays de HUD/editor/solver/lista.

---

## 3. A sessão de nível (`GameWorld`) e a invariante do grid

`GameWorld` (`Core/`) é **uma sessão de nível**: um `World` (Arch) + `GridManager` + `History` + `SpatialQuery`. Todo nível — inclusive a raiz — é a mesma abstração; não há "hub" especial.

### A invariante central: grid ≡ entities `Solid`

`GridManager` é um mapa `Entity?[,,]` célula→ocupante, **a fonte única de verdade sobre colisão espacial** — o ECS não rastreia ocupação por conta própria. Duas regras:

- `IsOccupied` devolve **true para fora dos limites** → o chamador trata a borda do grid como parede.
- **`GameWorld.Move`/`Occupy`/`Vacate` são os únicos mutadores legais de posição + ocupação juntas.** Eles mantêm `GridPosition` e o grid em sincronia e são *footprint-aware* (uma `BigBox` ocupa duas células — ver `FootprintOffsets`). **Todo novo mover tem que passar por eles.**

`FootprintFree(e, x, y, z)` responde "todas as células do footprint de `e` nessa âncora estão livres?" — usado pela queda e pelo undo, que erguem a peça do grid antes de checar.

`PlayerFell` marca o player congelado (caiu no chão-morte ou um bloco toggle apareceu nele): o movimento é ignorado até `Z`/`R`/`T`.

---

## 4. A árvore de níveis

Não existe hub especial: **todo nível é um `Level`** que pode ter portais (filhos) e/ou objetivos (meta que conclui). O que chamamos de hub é só o nível-raiz (id 0), que por acaso tem vários portais e nenhum objetivo.

### `LevelNavigator` — navegação como pilha de sessões

Dono de **uma pilha de sessões** (o caminho atual na árvore) e de **um cache por id** (a preservação). O topo é a sessão ativa.

| Ação | Tecla | Efeito na pilha | Cache |
|---|---|---|---|
| `TryEnterPortal` | Enter sobre `LevelPortal` | empilha um filho | mantém no cache |
| `CompleteActive` | pisar na meta | **popa e descarta** | **remove** → próxima visita reconstrói do zero |
| `SuspendActive` | `T` | popa | **mantém** → reentrada restaura estado exato |
| `JumpTo` | `,` `.` / lista | desempilha até a raiz, empilha o destino | mantém tudo vivo |
| `Reload` | editor mexeu nos ids | descarta tudo e recomeça da raiz | limpa |

O nível-pai fica **intacto na pilha** enquanto você está num filho — caixas, posições e histórico — **sem serializar nada**, porque cada sessão é um `World` isolado. `RefreshPortals` re-sincroniza a cor "concluído" dos portais com o registro global (`LevelCatalog._completed`) a cada troca.

### `LevelManager` — serviço sem estado que materializa receitas

`LoadLevel(session, level)` destrói as entidades anteriores, redimensiona o grid, e cria uma entity por spawn da receita (obstáculos, player, caixas, portais-caixa, big boxes, objetivos, inimigos, portais de nível, placas, toggles, bases atemporais, trilhos), chamando `session.Occupy` para as sólidas. Ao final roda `PressurePlateSystem.Resolve` (uma peça pode nascer sobre uma placa).

- `Restart` (`R`) — **reposiciona** as entidades existentes para o `SpawnPosition` e **grava o restart como um turno** no histórico (então o `Z` desfaz o próprio restart). Os handles de `Entity` seguem válidos.
- `FullReset` (`F`) — recarrega do zero (`LoadLevel`) e **limpa o histórico**; nada sobra pra desfazer.

---

## 5. Convenções de ECS (Arch)

**Componentes** (`ECS/Components/GameComponents.cs`) são `struct`s puros. Duas famílias de tag governam tudo:

- **`Solid`** = "ocupa o grid". `World.Has<Solid>(e)` é a resposta a "isto bloqueia movimento?". Uma frágil quebrada **perde `Solid` mas a entity persiste** (o undo re-solidifica).
- **`CellMarker`** = ocupa uma célula *lógica* mas **não** o grid — dá pra pisar em cima: `Objective`, `LevelPortal`, `PressurePlate`, `TimelessBase`, `Rail`.

**Sistemas** (`ECS/Systems/`) são instanciados à mão (não um `SystemGroup` do Arch) e chamados explicitamente do `Game1`. Regra dura do Arch: **mudança estrutural (Add/Remove componente, Destroy) é ilegal durante `World.Query`** — colete as entities primeiro, mute depois (ver `BreakBox`, `Restart`, `PressurePlateSystem.Resolve`).

`SpatialQuery` centraliza os lookups espaciais: `CellWith<T>(x,y,z)` (para marcadores, que não saem no `Occupant` do grid) e `First<T>()` (singletons como o player).

### Catálogo de peças

| Tipo de caixa (`BoxType`) | Peso | Regra |
|---|---|---|
| `Light` | 0 | carregada de graça, até por outra caixa |
| `Medium` | 1 | player empurra até 2 em fila |
| `Heavy` | 2 | só uma por vez |
| `Fragile` | 0 | empurra como leve; **quebra** prensada, ou quando a peça que repousava em cima sai (`Fragility`) |
| `Permanent` (verde) | 0 | empurra como leve, mas **o undo não a desfaz** (só `R`) |
| `Portal` | 0 | teleporta quem tenta entrar pro lado oposto da parceira de mesmo `Group` |
| `Magnetic` | 1 solta | encostou no player, **vira parte do corpo dele** (`Magnetism`) |
| `Collectible` | 0 | tocada DIRETO some (conta pra liberar a meta); empurrada em cadeia anda como leve |

Força do player: `PlayerPushStrength = 2` (soma dos pesos numa fila). Outros: `BigBox` (footprint de 2 células), `Obstacle` (terreno; `Normal`/`Sticky`), `Toggle` (bloco derivado de placas), `Rail` (só troca carga pelas pontas).

---

## 6. A regra de ouro: o turno canônico (`MovementSystem.Step`)

`MovementSystem.Step(GameWorld, dx, dz)` é **a única definição de um turno**. O input do jogo (`Update`, um wrapper fino de detecção de borda de teclado) e o solver/testes headless chamam o mesmo `Step`/`Undo`. Reimplementar uma regra fora dele faz o solver "provar" um jogo que não existe.

### Anatomia de um turno

```
Step(dx, dz)
 ├─ player caído? → ignora
 ├─ há magnética grudada?  (Magnetism.Attached — derivado da adjacência)
 │    ├─ comando alinhado ao olhar → TryMoveBody   (translação do corpo rígido)
 │    └─ comando perpendicular      → TryRotateBody (giro de ¼ de volta)
 └─ livre → Snapshot; olhar acompanha o comando; TryMovePlayer
                  │
                  ▼
            PushInto(...)   ← a regra recursiva única do movimento
                  │
                  ▼
           SettleAndCommit(player, before)
```

**`PushInto(x,y,z, dx,dz, budget, visited, direct, cargo)`** resolve *onde uma peça que quer a célula (x,y,z) de fato pára* (ou `null` = impossível), e dela emergem empurrão e teleporte **sem casos especiais**:

- célula livre → é ela mesma;
- caixa à frente → empurra recursivamente (mesmo `PushInto`); quem vinha herda a célula liberada. O `budget` (peso) decresce ao longo da fila;
- caixa `Portal` → redireciona pro lado oposto da parceira (recursão com `visited` pra não encadear teleporte infinito). Empurrar uma caixa **contra** um portal teleporta a própria caixa;
- frágil que não avança → **quebra** (`BreakBox`); coletável tocada `direct` → some;
- `BigBox` → regra própria (`PushBigBox`), trava em vez de encadear.

Só há mutação nos caminhos que terminam em sucesso — um `null` deixa o snapshot intacto. `cargo` distingue player (o trilho nunca o barra) de caixa; `direct` distingue toque do player de empurrão em cadeia (importa para `Collectible`).

### Corpo rígido magnético

Com uma magnética grudada, o player vira um **tanque** (estilo *Stephen's Sausage Roll*): comando **alinhado** ao olhar translada o corpo inteiro (`TryMoveBody`); **perpendicular** gira ¼ de volta (`TryRotateBody`), com as caixas varrendo o arco. Por isso **`Facing` faz parte do estado de busca**. Se uma peça do corpo trava, `RollbackAttempt` desfaz *tudo* (inclusive empurrões em cadeia) — jogada impossível não deixa rastro. Portal desprende uma caixa presa: quem atravessa assenta longe do corpo e o grude derivado se desfaz sozinho.

### `SettleAndCommit` — a ordem determinística do turno

```
1. Gravity.Settle           — Y é SEMPRE derivado, nunca escolhido (cai até apoiar)
2. PressurePlateSystem.Resolve — re-deriva toggles da ocupação das placas
3. Fragility.ResolveDepartures — frágeis que perderam a carga quebram (e re-Resolve)
4. StartTeleportAnims       — anima quem teleportou (só escreve componentes)
5. CommitTurn(before)       — grava o delta líquido de cada peça no History
6. TimelessBase → Forget    — quem terminou sobre uma base atemporal tem a pilha expurgada
7. y==0 → PlayerFell         — player que assentou no chão-morte congela
```

---

## 7. Histórico e undo (`History`)

`History` é **uma pilha de `Move` por peça**. Todo turno, **toda** peça reversível empilha um `Move` (deslocamento líquido `Dx,Dy,Dz` + `SolidChange` + `Facing` do início do turno) — mesmo quem ficou parado grava inércia (delta zero), pra manter as pilhas **alinhadas por turno**. `Undo` popa o topo de cada pilha e anda por `-(delta)`.

```
turno →  ┌────┐ ┌────┐ ┌────┐
player   │ +1 │ │ 0  │ │ -1 │   ← pilha do player
box A    │ 0  │ │ +1 │ │ 0  │   ← pilha da caixa A
         └────┘ └────┘ └────┘
           t0     t1     t2      Undo popa a coluna t2 de todas ao mesmo tempo
```

O undo é **simultâneo**: ergue do grid todas as peças sólidas do lote **antes** de reposicionar qualquer uma (padrão de duas fases, igual ao `RollbackAttempt`), senão o destino de uma sobrescreveria a célula que outra ainda ocupa.

Dois furos deliberados na simetria do undo:

- **`Forget`** (placa atemporal `TimelessBase`) — esvazia a pilha de quem pisa nela: fica commitado ali, o passado some, mas segue empilhando adiante.
- **Caixa verde (`Permanent`)** — **nunca entra no snapshot** (excluída em `Snapshot`), então o undo não a toca; só o `R` a reverte.

`Capture`/`Restore` fotografam/restauram todas as pilhas (usados pelo solver no tier atemporal, onde o histórico entra na chave de busca). `LastReverted` expõe o que o último undo desfez, sem o histórico saber o **porquê** — é como a animação de teleporte redescobre os portais consultando o mundo (`AnimateUndoTeleports`).

---

## 8. Estado derivado nunca é armazenado nem historizado

Dois estados são **função pura das posições** e por isso ficam fora do `History`:

- **Solidez dos blocos `Toggle`** — `PressurePlateSystem.Resolve` re-deriva todo frame a partir da ocupação das placas (`Threshold`: 1 = OR, total do grupo = AND). É autoridade única sobre o `Solid` dos toggles. Um bloco que apareceria sobre uma célula ocupada fica **pendente**; se quem ocupa é o player, ele congela.
- **Adjacência magnética** — `Magnetism.Attached` deriva da posição a cada frame; nada é guardado.

O undo restaura só as **posições**, e o `Resolve` (rodado todo frame pelo `Game1`) re-deriva o resto. Consequência prática: o histórico **nem sabe que toggles existem**.

---

## 9. Helpers de mecânica (`Core/`)

Fragmentos de regra que o `Step` compõe — cada um um helper pequeno e (quase) puro, pra empurrão, queda e giro nunca discordarem:

| Helper | Responsabilidade |
|---|---|
| `Gravity` | "se der pra cair, cai" — assenta todas as peças móveis de baixo pra cima; empate de Y processa o player primeiro (pra magnética cair junto, não pairar órfã). |
| `Magnetism` | adjacência derivada; `Attached` (caixas do corpo) e `IsHeld` (não cai com o corpo). |
| `Stickiness` | obstáculo sticky segura a peça vizinha contra o **afastamento** direto (`Holds`, `HoldsAgainstFall`). |
| `Rails` | carga entra/sai só pelas **pontas** do trilho (`RejectsEntry`, `RejectsExit`, `Holds`); nunca barra o player. |
| `Restraints` | agrega "esta caixa está retida?" (sticky atrás, trilho que não sai nessa direção). |
| `Fragility` | captura as frágeis "armadas" no início do turno e resolve as que perderam a carga. |
| `Collectibles` | `AllCollected` — a meta fica desabilitada enquanto houver coletável pendente. |

---

## 10. Render e animação

O engine é **instantâneo** (a `GridPosition` muda de uma vez); só o **desenho** é interpolado. Isso mantém a lógica idêntica entre o jogo e o headless — o render é uma camada por cima que os chamadores headless simplesmente ignoram.

- **`RenderPosition`** (Vector3) é a posição *visual*, separada da célula lógica. **`RenderFacing`** (yaw) é o olhar visual.
- **`MoveAnimationSystem.Update`** desliza a `RenderPosition` até a célula com suavização exponencial (ease-out, independente de frame rate). Movimento em "L": desliza no plano X/Z **e depois** cai no Y. Dirige três animações especiais via componentes escritos pelo `Step`:
  - **`TeleportAnim`** — duas metades (sugado pra dentro do portal de entrada / brota no de saída);
  - **`OrbitAnim`** — a caixa que varre o arco do giro magnético (e gira em torno do próprio eixo);
  - o giro do olhar (`RenderFacing` → `Facing`).
  - `SnapAll` encaixa tudo sem interpolar (usado pelo `F` e ao entrar no solver).
- **`RenderSystem.Draw`** desenha, em ordem: chão-morte → obstáculos → toggles → marcadores (tiles planos) → entidades (na `RenderPosition`). O "nariz" branco do player mostra o olhar; a presilha laranja (`DrawMagnetLinks`) mostra o grude. Cores por tipo são compartilhadas com o editor (`ColorOf`).
- **`GridView`** é a conversão única grid→mundo (centra o grid na origem X/Z; `rise` posiciona cada tipo na célula) — render e animação nunca divergem.
- **`Camera`** (fixa, isométrica) e **`EditorCamera`** (orbitável) tiram o enquadramento do mesmo `CameraFraming` — nunca dessincronizam. **`CubeRenderer`** (preso ao `GraphicsDevice`) é o único desenho de baixo nível; é compartilhado entre jogo e editor.

---

## 11. O solver (`Solver/`)

Dev tool que **prova solvabilidade** e devolve uma sequência de ações. O núcleo é **100% livre de MonoGame**; só `SolverTool`/`SolverRenderer` (playback in-game) tocam o engine gráfico.

### `SolverSim` — o adaptador headless

Possui uma sessão *scratch* (`GameWorld` + `LevelManager`, sem `GraphicsDevice`) e traduz entre ela e o `SolverState`. As transições são o **mesmo `Step`/`Undo`** do jogo. Padrão de expansão de nó: `Restore(estado)` → `Apply(ação)` → `Capture()`. Duas otimizações de espaço de estado:

- **classes de intercambialidade** — caixas planas do mesmo tipo são indistinguíveis; o `Capture` ordena os slots de cada classe, colapsando permutações (até *n!* de redução). Só no tier sem pilhas.
- **`_trackFacing`** — `Facing` só entra no estado quando pode mudar uma transição (magnética ou tier atemporal); fora disso é constante, fundindo os 4 olhares.

### `SolverState` — chave de busca canônica e hashável

Posição + solidez de cada peça, olhar, `PlayerFell` e — só no tier atemporal — as pilhas do histórico. Estado derivado (solidez de toggles, Y de queda) **fica fora**: dois `SolverState` iguais são o mesmo estado de jogo. Hash pré-computado no construtor.

### `PuzzleSolver.Solve` — três tiers

```
tem objetivo? ──não──► NoObjective (hub, nada a resolver)
   │sim
   ▼
requer undo? (TimelessBase ∨ Permanent)
   │                        │
  não                      sim
   ▼                        ▼
tem magnética?         tenta Macro primeiro (barato); se falhar → IDDFS
   │        │              (+ ação Undo; pilhas entram no estado)
  não      sim
   ▼        ▼
 MACRO     BFS por passos
```

- **Macro (padrão)** — comprime a caminhada inerte (`SolverReach`: flood fill das células onde entrar/sair não muda nada) em macro-ações; cada ação é um passo de **fronteira** (empurrão, placa, portal…) executado pelo engine. Melhor-primeiro por uma heurística de placas (`PressHeuristic`) — só prioridade, **nunca poda**: completo e com prova de insolúvel por esgotamento. Caminho re-validado por replay antes de sair (válido, sem garantia de mínimo).
- **BFS por passos** — quando uma magnética torna a caminhada não-inerte (posição+olhar mudam as transições). Caminho mínimo garantido.
- **IDDFS + Undo** — níveis atemporais: as pilhas crescem com o caminho (BFS estouraria memória). Um undo que não cruzou `Forget`/verde volta a um estado byte-idêntico e o visited poda sozinho → só undos **significativos** sobrevivem.

`SolverLimits` (nós/tempo/profundidade): estourou → `LimitHit = "não sei"` (parou por limite, não por prova).

---

## 12. Testes (`Sokoban3D.Tests/`)

100% headless, sobre o **mesmo engine** (`TestLevels.Spawn` = `GameWorld` + `LevelManager`, sem `GraphicsDevice`). Rodam em série (`DisableTestParallelization` — o Arch tem pool global de worlds).

- **`SolvabilityOracleTests`** — o oráculo: **todo `Maps/*.json` com objetivo tem que ser provado resolvível**, pela busca **ou** pelo replay de um certificado `Solutions/level_N.moves` (gravado sozinho ao vencer o nível no jogo, via `SolutionRecorder`/`SolutionStore`). Rede de regressão: uma mudança de regra ou um mapa editado que quebre um nível derruba o build com o id na cara.
- **`EnginePropertyTests`** — fuzz de invariantes: grid ≡ entities `Solid`; nenhuma célula com duas `Solid`; player nunca termina em parede; undo é inverso exato **exceto** através de `Forget`/verde; `Settle(Settle(w)) == Settle(w)`; `Restart` devolve exatamente os spawns.
- **`SolverBasicTests`** — sanidade dos tiers do solver.

Rodar: de `Sokoban3D.Tests/`, `dotnet test`; um caso: `dotnet test --filter "MapWithObjectiveIsSolvable(id: 7)"`.

---

## 13. Editor (`Editor/`) — dev tool

`Tab` alterna jogo/editor. Edita um **clone** da receita ativa e, a cada mudança, **re-materializa a sessão** via `LevelManager.LoadLevel` — reaproveitando todo o ECS e o render do jogo. O mouse é a via principal (picking de voxel: esquerdo coloca, direito apaga, meio copia brush, Ctrl+esquerdo move, roda troca a camada Y; Alt+arrasto orbita). Toda mutação passa por snapshot (Ctrl+Z/Ctrl+Y). Salvar grava de volta no `Maps/level_<id>.json` — **editar altera o mapa oficial**. Uma checagem de solvabilidade roda o solver em segundo plano (`EditorValidation`).

---

## 14. Telemetria (`Analytics/`) — opcional

`LevelAnalytics` rastreia uma **tentativa por nível** e traduz os eventos do loop (entrar, mover, undo, restart, vencer, sair) em eventos de domínio. Casado com a árvore: entrar num filho **suspende** o cronômetro do pai; concluir **fecha** a tentativa; sair do jogo fecha as abertas como abandono ("drop"). O sink é o GameAnalytics **se** houver `analytics.json` (git-ignored), senão um `NullAnalytics` — no-op, não afeta o jogo. O raiz (hub, sem meta) não é rastreado.

---

## 15. Persistência e conteúdo

- **`Maps/level_<id>.json`** é a fonte de verdade. `LevelRepository` lê/escreve (resolve `Maps/` relativo ao cwd); `LevelSerializer` faz o formato (leitura via `System.Text.Json` com DTO + converters; escrita à mão pra o arquivo ficar legível — uma célula por linha, `[x,y,z]` ou `[x,y,z,"Tipo"]`); `LevelCatalog` guarda o registro global de conclusão por id. `SwapIds` reordena (troca ids/arquivos em disco; portais não são remapeados).
- **`Content/Content.mgcb`** é o content project do MonoGame (texturas, fonte `Hud`), compilado no `dotnet build`. Editar com `mgcb-editor` (via `dotnet tool restore`).

---

## 16. Princípios que amarram tudo

1. **Um engine só, dois chamadores.** `Step`/`Undo` são a regra; jogo e solver/testes chamam o mesmo. Nunca uma segunda cópia.
2. **Dependência unidirecional.** Ferramenta → engine, nunca o contrário. O núcleo do solver é livre de MonoGame.
3. **O grid é a verdade espacial.** Só `GameWorld.Move`/`Occupy`/`Vacate` mutam posição+ocupação; `Solid` responde "ocupa o grid?".
4. **Estado derivado não se armazena nem se historiza.** Toggles e magnetismo re-derivam da posição todo frame.
5. **Y é derivado, nunca escolhido.** A gravidade decide a altura.
6. **Render é uma camada ignorável.** `RenderPosition`/anims só escrevem componentes; headless não os consome.
7. **Idioma:** comentários/docs em português, identificadores em inglês.
8. **Atalhos novos** (teclado 60%/TKL): sem setas, PgUp/PgDn/Home/End nem F-row; bloco alfanumérico (nav vertical = W/S).
