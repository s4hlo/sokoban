# Caixa Portal — Design

## Objetivo

Adicionar um novo tipo de caixa, o **Portal**, que vive em pares ligados por um `Group`.
Quando algo (player ou caixa empurrada) tenta entrar na célula de um portal, em vez de
ocupá-la, **atravessa** e reaparece do lado oposto do portal parceiro. Se a saída estiver
bloqueada, o portal volta a ser uma caixa empurrável comum.

## Comportamento

Player no cell P anda na direção D (no plano X/Z) contra o portal A:

1. Acha o parceiro B = a outra entity com o mesmo `Group`.
2. Célula de saída `L = B + D` (lado oposto de B em relação à entrada — portal clássico).
3. Tenta liberar L ("empurra o que estiver" lá, com a força do player). Liberou → o que
   se movia reaparece em L. A direção D é preservada.
4. Os portais A e B **não saem do lugar** nesse caso.

**A regra é nativa, não um conjunto de casos especiais.** Teleportar, empurrar através do
portal, e empurrar o próprio portal são todos o mesmo fluxo recursivo:

- **Teleporte do player** — o passo do player cai num portal, atravessa, reaparece na saída.
- **Empurrar uma caixa através do portal** — empurro uma caixa cujo `cell + D` é um portal; a
  recursão teleporta *a caixa*. Ela some pela entrada e reaparece na saída; quem empurrava
  ocupa o lugar que ela deixou.
- **Empurrar o próprio portal** — só quando a saída está bloqueada (parede, fora do grid, ou
  célula não-liberável). Aí a recursão segue pro ramo "caixa" e empurra o portal `cell + D`, se
  houver espaço. É o fall-through, não um `if` à parte.
- **Portal sem par** — nunca acha saída; é só uma caixa empurrável. Natural.

## O núcleo: uma função recursiva

Hoje o `MovementSystem` tem `TryClear` (retorna `bool`: "a célula ficou livre?") e o chamador
move o player pra dentro da célula-alvo. Com portal, o que entra não fica na célula do portal —
reaparece em outro lugar. Então a função passa a **devolver onde a peça de fato parou**:

```
// "Algo quer ocupar `cell` vindo na direção D. Resolve tudo (empurrão, teleporte, ou os dois
//  encadeados) e devolve a célula onde de fato pára, ou null se for impossível. Só muta o mundo
//  nos caminhos que dão certo."
GridPosition? PushInto(cell, D, budget, visited):
    if !valid(cell):  return null          // parede / fora do grid
    if empty(cell):   return cell          // entra direto

    occ = occupant(cell)

    if occ é portal E tem parceiro E ainda não foi visitado nesta jogada:
        marcar occ como visitado
        exit = parceiro(occ) + D
        landing = PushInto(exit, D, budget, visited)   // tenta atravessar
        if landing != null: return landing             // ATRAVESSOU

    if occ é caixa (inclui portal sem saída usável):
        w = peso(occ); if w > budget: return null
        fwd = w == 0 ? 0 : budget - w
        boxLanding = PushInto(cell + D, D, fwd, visited)   // pra onde a caixa/portal vai
        if boxLanding != null:
            Move(occ, boxLanding); return cell             // empurrou; quem vinha ocupa `cell`
        if occ é frágil: Break(occ); return cell
        return null

    return null                            // player / inimigo / obstáculo: bloqueia
```

`PushInto` substitui o `TryClear` atual. Para níveis sem portal ele reproduz o `TryClear` passo
a passo (caixa leve, fila de médias, frágil que quebra) — é uma generalização, não uma reescrita
do comportamento existente.

**Terminação.** Dois portais podem encadear o teleporte (a saída de um cai noutro). O conjunto
`visited` (portais já atravessados *nesta jogada*) impede recursão infinita num arranjo cíclico:
reentrar num portal já visto pula o teleporte (cai no empurrão). É a memória natural da recursão,
não um tratamento de caso. O empurrão termina por esgotamento de espaço no grid.

## Undo

**Zero código de portal no histórico.** O `Move` muda posições; o `Snapshot`/`CommitTurn` que já
existe grava o delta líquido de cada peça (player e caixas, inclusive as teleportadas) e o Z
inverte aplicando o delta oposto. A gravidade pega quem mudou de posição via `MovedSince`. O
histórico não sabe o que é portal nem pra onde a peça foi — só o deslocamento.

## Estrutura de dados

Segue os padrões existentes (Plate/Toggle usam `Group`; Box usa `Type`):

- `BoxType.Portal` no enum — dá cor própria no render e peso em `BoxRules`. Peso **0** (empurrada
  de graça quando indireta/sem saída).
- Novo componente `PortalBox { int Group; }` carregando o pareamento.
- Entity no spawn: `GridPosition + Box{Type=Portal} + PortalBox{Group} + SpawnPosition +
  RenderPosition + Solid`. Assim gravidade, restart, undo e render funcionam de graça (é uma
  caixa sólida com spawn como as outras).

## Toque nos arquivos

- **`ECS/Components/GameComponents.cs`** — `BoxType.Portal`, struct `PortalBox`, case no
  `BoxRules.Weight`.
- **`ECS/Systems/MovementSystem.cs`** — refatorar `TryClear` → `PushInto` (devolve célula de
  pouso), com o ramo do portal e o helper `FindPartner`. `TryMovePlayer` usa o pouso devolvido.
- **`ECS/Systems/RenderSystem.cs`** — cor da caixa Portal em `ColorOf`.
- **`Levels/LevelManager.cs`** + `Level` — lista `PortalBoxSpawns (x,y,z,group)`, spawn da entity
  e inclusão no `Clone`.
- **`Levels/LevelSerializer.cs`** — array `"PortalBoxes"` com `[x,y,z,group]` (Save, DTO,
  converter, FromDto).
- **`Editor/EditorBrush.cs`** + **`Editor/LevelEditor.cs`** + **`Editor/EditorRenderer.cs`** —
  pincel novo ciclando o `Group` no mesmo padrão de Plate/Toggle, com cor, rótulo e tag.

## Fora de escopo (YAGNI)

- Tint por grupo (cada par com cor distinta). Uma cor só pra todos os portais por enquanto.
- Cooldown/animação de teleporte. O movimento usa o mesmo `RenderPosition` interpolado das caixas.
- Inimigos atravessando portais (inimigos não se movem ainda no jogo).
