# Obstáculos + gravidade (flavor 3D) — design

## Objetivo

Dar profundidade vertical real ao jogo: obstáculos fixos empilháveis formam o
terreno, o player anda em cima deles, e pisar no vazio faz cair. A queda usa
gravidade de verdade (cai até pousar). Cair até o fundo ("chão-morte") congela o
player, que só sai disso com Z (undo), R (restart) ou T (suspender).

## Decisões (validadas com o usuário)

- **Obstáculos são o chão.** Não há piso universal; o terreno é feito de
  obstáculos (voxels sólidos 1×1×1). Estáticos, fixos por nível.
- **Chão-morte no fundo.** Plano sólido sob o terreno. O player que repousa
  direto nele morre/congela. Não é abismo infinito.
- **Gravidade real ("cai até pousar").** A peça despenca célula a célula até
  achar algo sólido embaixo (obstáculo, caixa ou o limite inferior do grid).
- **Sem escalada.** Obstáculo no nível da peça é parede: bloqueia o movimento
  horizontal. Só se desce, caindo.
- **Caixas também caem.** Empurrada pro vazio, a caixa despenca até pousar e
  vira terreno (dá pra ficar em cima). Caixa que chega ao chão-morte **não
  quebra** — só fica lá.
- **Estado "caiu".** Player no chão-morte perde o movimento; só Z/R/T resolvem.

## Modelo espacial

- Y cresce pra cima. Convenção:
  - **y = -1 (fora do grid, embaixo): chão-morte.** Já é "sólido" pela regra
    existente `IsOccupied == true` para fora dos limites. Desenhado como um plano
    escuro/ameaçador sob o terreno.
  - **y >= 0: terreno (obstáculos) e peças.** Os níveis existentes ganham um piso
    cheio de obstáculos em y=0 e as peças sobem pra y=1.
- **Suporte:** a célula (x,y,z) está apoiada se `IsOccupied(x, y-1, z)` (obstáculo,
  caixa, ou o limite inferior do grid em y-1 < 0).
- **Gravidade:** após um movimento, cada peça afetada cai (y--) enquanto não
  estiver apoiada.
- **Morte do player:** ocorre sse o player repousa em **y == 0** (apoiado só pelo
  chão-morte). Caixa em y=0 sobrevive e vira terreno.

## Mudanças por arquivo

- **ECS/Components/GameComponents.cs** — novo `struct Obstacle` (tag estática).
- **Core/GridView.cs** — `ToWorld` passa a usar o Y do grid pra altura de mundo;
  constantes de "subida" por tipo (peça, obstáculo, marcador).
- **Levels/LevelManager.cs** — `Level.ObstacleSpawns` + helper `FillFloor`; spawn
  de obstáculos (ocupam o grid); `RenderPosition` inicial com Y.
- **Levels/LevelCatalog.cs** — migra os 4 níveis: `Height` >= 2, piso de
  obstáculos em y=0, peças/objetivos/portais em y=1.
- **ECS/Systems/MovementSystem.cs** — após o empurrão horizontal, aplica
  gravidade ao player e às caixas movidas; marca morte do player em y==0.
- **ECS/Systems/MoveAnimationSystem.cs** — alvo de interpolação usa o Y do grid
  (a queda anima de graça pelo lerp existente).
- **ECS/Systems/RenderSystem.cs** — desenha o chão-morte (plano escuro no fundo),
  uma passada de obstáculos (cubos cheios), marcadores/peças com Y; player
  "caído" com cor distinta.
- **Core/GameWorld.cs** — flag `PlayerFell` (estado congelado da sessão).
- **Game1.cs** — Z/R/T recalculam/limpam `PlayerFell`.

## Undo

A queda é parte da MESMA ação do movimento que a causou: o `History` já guarda a
posição anterior da peça e o `TryClearForUndo` já lida com `dy`. Um Z reverte
movimento + queda de uma vez.

## Fora de escopo (por ora)

- Inimigos com IA / gravidade de inimigo.
- Empilhar caixas como mecânica de puzzle dedicada (funciona, mas sem níveis que
  a explorem ainda).
- Editor de níveis.
