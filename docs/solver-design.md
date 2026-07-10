# Spec — Solver de puzzles + ecossistema de testes (dev tools)

> Documento de design, implementado nas etapas (a)–(d) do §10: `MovementSystem.Step/Undo`
> headless, `Solver/` (núcleo puro + tool in-game na tecla P) e `Sokoban3D.Tests/`.
> Decisão fechada da questão §9.1: gate do Undo = `TimelessBase ∨ Permanent`.
> Convenção do repo: identificadores em inglês, comentários/docs em português.

## 1. Objetivo

Ferramentas de desenvolvimento (dev tools, como o modo editor) para:

1. **Provar solvabilidade** — dado um `Level`, dizer se é resolvível e devolver uma
   sequência mínima de ações.
2. **Playback in-game (modo A)** — depois de terminar (ou durante o design de) um
   nível, assistir o solver executando a solução dentro do jogo, com as animações reais.
3. **Ecossistema de testes** — oráculo de solvabilidade sobre todos os mapas + testes
   de propriedade sobre as regras, pra pegar níveis quebrados e regressões de mecânica.
4. (Futuro) **Métricas de dificuldade e geração** — o solver como função de fitness.

Princípio norteador: são **dev tools**. Não precisam ser excluíveis do build hoje,
mas a estrutura deve permitir extraí-las/lançá-las separadamente no futuro:

- Cada tool em sua pasta/namespace próprio (como `Editor/`).
- Dependência em um sentido só: **tools → engine, nunca engine → tools**.
- O núcleo do solver 100% livre de MonoGame.

## 2. O que o solver precisa modelar (regras vigentes)

Condição de vitória: **o player pisar numa célula de `Objective`**
(`Game1.PlayerOnObjective`). Caixas não são a meta — são ferramentas/portões.

Um turno completo, todo determinístico (ordem exata em
`MovementSystem.SettleAndCommit`):

1. Comando horizontal (4 direções) → empurrão recursivo (`PushInto`) com orçamento
   de peso (`PlayerPushStrength = 2`, pesos em `BoxRules`).
2. Caixa portal pode redirecionar o passo (teleporte) dentro do próprio `PushInto`.
3. Frágil que não avança **quebra** (perde `Solid`; irreversível dentro do turno,
   reversível pelo undo).
4. **Gravidade** (`Gravity.Settle`) — Y é sempre estado derivado; nunca se escolhe.
5. **Placas de pressão** (`PressurePlateSystem.Resolve`) — solidez dos `Toggle` é
   derivada da ocupação, nunca armazenada.
6. Commit no histórico (deslocamento líquido por peça + `SolidChange`).
7. `TimelessBase`: quem terminou o turno sobre ela tem a pilha expurgada (`Forget`).
8. Player que assenta em `y == 0` → `PlayerFell` (estado morto pro solver).

**Magnetismo** muda o espaço de ações: com caixa magnética grudada (adjacência
derivada, nada armazenado), o player é corpo rígido — comando alinhado ao olhar
translada, perpendicular gira. Logo **`Facing` faz parte do estado de busca**.

**Caixa verde (`Permanent`)**: nunca entra no histórico → o undo é assimétrico em
volta dela mesmo sem `TimelessBase` (ver questão aberta §9).

## 3. Peça central: o passo headless (`Step`)

A única mudança no engine. Hoje `MovementSystem.Update(session, KeyboardState)`
mistura leitura de teclado, regras e agendamento de animação.

Extração:

```
MovementSystem.Step(GameWorld session, Direction dir)   // sem teclado, sem render
```

- O mapeamento tecla→direção (`GetFreshDirection`) sobe pro chamador: o input do
  jogo passa a direção; o solver passa a direção.
- O agendamento de animação (`MarkTeleport`/`StartTeleportAnims`, `TeleportAnim`,
  `RenderPosition`) fica atrás de um seam que é no-op em modo headless. Não corrompe
  a simulação (a `GridPosition` já é síncrona), mas é trabalho/lixo desnecessário.

**Regra de ouro: um engine só, dirigido de dois jeitos.** O solver NÃO reimplementa
regra nenhuma — senão diverge (portais, giro magnético, ordem da gravidade) e passa
a "provar" um jogo que não existe. `GameWorld`/`LevelManager` já instanciam sem
`GraphicsDevice`; headless é extração, não rewrite.

## 4. Modelo de estado e busca

### Ações

- Sempre: `Up / Down / Left / Right`.
- `Undo` (tecla Z): **só quando o nível contém `TimelessBase`** (é mecânica de
  puzzle nesses níveis; ver §9 sobre `Permanent`).
- `R`/`F` (restart/reset): **fora do solver** — são conveniência, não mecânica.

### Estado canônico (hashável)

```
player(x, y, z) + facing(dx, dz)
+ [caixas: (x, y, z, tipo, quebrada?)] ordenadas
```

- Y entra no estado mas nunca é escolhido (gravidade deriva).
- Solidez dos toggles NÃO entra: é derivada das posições.
- `Facing` entra por causa do modo tanque magnético.
- `PlayerFell == true` → estado morto, poda.

### Busca em dois níveis

| Nível | Chave de dedup | Ações | Algoritmo |
|---|---|---|---|
| **Sem timeless** (maioria) | só o mundo | 4 movimentos | BFS / A* (heurística: distância ao objetivo mais próximo) |
| **Com timeless** | mundo **+ pilhas do histórico** | 4 movimentos + `Undo` | IDDFS (limitado por profundidade — as pilhas crescem com o caminho) |

Insight que mantém o caso timeless tratável: `move` seguido de `Undo` volta a um
estado byte-idêntico (mesmo mundo, mesmas pilhas) → o visited-set poda sozinho.
Um `Undo` só sobrevive como nó NOVO quando cruzou um `Forget` (ou uma verde/frágil)
— ou seja, exatamente quando a mecânica está fazendo algo. A busca se auto-seleciona
pra undos significativos, sem heurística especial.

### Saída

```
SolveResult { bool Solvable; List<SolverAction> Path; Stats (nós explorados, profundidade, tempo) }
```

## 5. Playback in-game (modo A)

- O caminho do solver é uma lista de ações; playback = **auto-player** que
  desenfileira uma ação por batida de animação e a injeta como input sintético no
  MESMO `Step` que o teclado usa.
- `MoveAnimationSystem`/`RenderSystem`/animação de teleporte funcionam de graça —
  é um turno normal do jogo.
- Determinismo do engine único ⇒ o replay não pode dessincronizar do que o solver achou.
- Extras baratos: stepper (avançar um movimento por tecla), HUD com
  "Resolvido em N movimentos" / "Insolúvel (X estados explorados)".
- Fora do escopo (decidido): visualização da exploração (B) e bot com backtracking
  visível (C) — builds separados, talvez depois.

## 6. Layout de módulos

```
Sokoban3D/
├── Solver/                    ← novo, espelha Editor/
│   ├── PuzzleSolver.cs        ← PURO (sem MonoGame): busca → SolveResult
│   ├── SolverState.cs         ← PURO: estado canônico (+ pilhas em nível timeless)
│   ├── SolverAction.cs        ← PURO: Direction | Undo
│   ├── SolverTool.cs          ← como LevelEditor: Enter/Exit/Update, guarda o
│   │                             SolveResult, dirige o playback
│   └── SolverRenderer.cs      ← como EditorRenderer: HUD, reusa o CubeRenderer
```

- `Step` NÃO mora aqui — é refatoração do core (`MovementSystem`), usada pelo jogo
  e pelo solver.
- Fiação em `Game1`, como o editor: campo, construção no `Initialize`, tecla de
  toggle (uma livre do bloco principal — Tab já é do editor; teclado 60%, sem F-row).
- Consumidores do núcleo puro: (a) `SolverTool` in-game, (b) testes/oráculo em CI,
  (c) gerador futuro. Só `SolverTool`/`SolverRenderer` tocam MonoGame.

## 7. Ecossistema de testes

Projeto de teste novo (hoje não existe nenhum), rodando 100% headless sobre o
mesmo engine:

1. **Oráculo de solvabilidade**: roda o solver sobre todos os `Maps/*.json`; falha
   o build se algum nível ficou insolúvel. Rede de regressão pra mudanças de regra.
2. **Testes de propriedade / fuzz** (playouts aleatórios de ações válidas):
   - ocupação do grid ≡ posições das entities `Solid` (o contrato central do repo);
   - **undo é inverso exato, EXCETO através de `Forget`/`Permanent`** — teste de
     caracterização: longe de timeless/verde, `move`+`Z` volta byte-idêntico; perto,
     compara com resultado esperado especificado à mão (é a mecânica, pinada);
   - nenhuma célula com duas entities `Solid`; player nunca termina dentro de parede;
   - `Settle(Settle(w)) == Settle(w)` (gravidade idempotente);
   - `Restart` devolve exatamente os spawns.
3. **Linter de níveis**: portal-caixa sem par / grupo com 3+ (`FindPartner` não
   suporta), placa sem toggle do grupo (e vice-versa), objetivo soterrado por
   `Solid`, nível trivial (solução ≤ 1), e — via solver — **elementos redundantes**
   (remove uma caixa; ainda resolve no mesmo comprimento? é decoração).

## 8. Futuro: dificuldade e geração

O solver é a função de fitness. Métricas mensuráveis:

- comprimento mínimo da solução; razão empurrões/movimentos;
- quão *forçada* é a solução (nº de caminhos ótimos alternativos);
- proporção de movimentos que levam a estados mortos;
- uso efetivo de cada mecânica colocada (solução muda se remover o elemento?).

Geração/mutação de níveis mantendo só os que caem numa banda-alvo dessas métricas.

## 9. Questões abertas

1. **Caixa verde sem timeless**: existe (ou existirá) nível com `Permanent` e SEM
   `TimelessBase`? Se sim, o gate do `Undo` deve ser `TimelessBase ∨ Permanent`
   (senão o solver pode declarar insolúvel um nível vencível pela porta dos fundos
   do undo assimétrico — ou não ver uma solução não-intencional).
2. **Inimigos**: `Enemy { Speed }` existe mas não há sistema que os mova. Enquanto
   estáticos, são obstáculos pro solver. Se passarem a se mover, o solver precisa
   modelar adversário (problema bem maior) — revisitar esta spec.
3. **Escopo dos level-portals**: o solver resolve UM nível. Resolver a árvore
   inteira (hub → filhos) fica fora por ora.

## 10. Ordem de construção

1. **(a)** Extrair o `Step` headless (seam de animação incluso) — a pedra angular.
2. **(b)** Núcleo puro do solver (estado + BFS; IDDFS timeless em seguida).
3. **(c)** Projeto de testes: oráculo + primeiras propriedades (é o que "mata falha
   humana" mais cedo).
4. **(d)** `SolverTool` + playback in-game.
5. **(e)** Linter; depois métricas/geração quando o oráculo existir.

Cada etapa é útil sozinha; (a) destrava todas as outras.
