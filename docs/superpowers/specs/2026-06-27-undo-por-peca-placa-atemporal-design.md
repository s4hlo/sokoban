# Refactor do undo: expurgo de histórico por peça na placa atemporal

Data: 2026-06-27

## Problema

O undo atual carrega complexidade demais por causa da `TimelessBase` (placa
atemporal). Hoje a placa "congela" o undo de quem está em cima dela com uma
decisão **dinâmica** tomada no momento do Z: um `frozen` set, um `OnTimelessBase`
recalculado, e um empurrão em cadeia (`TryClearForUndo`) que desloca caixa verde e
peças sobre bases pra liberar a célula de volta. Isso gera perguntas que não
deveriam importar ("a placa estava ativa no passado?") e é frágil.

## Objetivo

Simplificar colossalmente o undo. A placa atemporal deixa de decidir nada no Z e
passa a **apagar o histórico daquela peça** no instante em que a peça pisa nela.
O undo vira trivial: dá pop na última ação e reverte posição/solidez das peças
gravadas, sem empurrar ninguém.

## Modelo (mantido)

- Pilha global de ações; cada ação é uma `List<MoveStep>` — a lista ordenada dos
  **movimentos** daquele turno. Cada `MoveStep` é peça + deslocamento `(Dx,Dy,Dz)` +
  uma transição de solidez (`None`/`Lost`/`Gained`). Guardar MOVIMENTO (e não posição
  absoluta) é o que deixa o undo ser um replay reverso, sem precisar reconciliar com
  peças fora do histórico. Um Z reverte a última ação inteira.
- Caixa verde (`BoxType.Permanent` / `IgnoresUndo`) segue sem histórico — nunca é
  gravada, só o R a reposiciona. **Inalterada.**
- Restart (R) empilha um snapshot, agora como passos de movimento (delta até o
  spawn) em vez de posições absolutas.

## Mudança 1 — `History.Forget(Entity)`

Novo método que remove as entradas daquela peça de **todas** as ações da pilha
(ações que ficarem vazias são descartadas). Efeito: a peça nunca mais é revertida
por nenhum Z futuro — fica "commitada" na posição atual. As outras peças seguem
com histórico intacto.

Ponto-chave: é um **expurgo único do passado**, não um flag persistente. A peça
não ganha nenhuma marca. Como o registro é só o `Push` normal por ação, assim que
a peça sai da placa o próximo passo já volta a ser gravado automaticamente. Não
confundir com a caixa verde, que tem o flag `IgnoresUndo` pra sempre.

## Mudança 2 — `History.Undo` como replay reverso, tratado como movimento normal

Sai tudo: o `frozen` set, `OnTimelessBase`, o `TryClearForUndo` (empurrão em cadeia)
e o dedup de posição da gravidade. O undo desfaz cada passo de trás pra frente,
movendo a peça por `-(delta)` **como qualquer outro movimento**:

- Movimento puro: libera a célula atual e ocupa a de volta **só se estiver livre**;
  inverte a transição de solidez (`Lost` → readiciona `Solid`; `Gained` → remove).
- Processar relativo à posição atual, do último passo pro primeiro, faz movimentos
  compostos (empurrão + queda) se desfazerem na ordem certa. Em jogo normal a célula
  de volta está sempre livre (o replay reverso é coerente).
- No fim, re-deriva os toggles (`Resolve` com record descartável).

**Não há tratamento especial pra peça fora do histórico.** Se a célula de volta estiver
ocupada por uma caixa verde ou por uma peça commitada numa placa atemporal, o movimento
reverso é simplesmente **bloqueado** — a peça fica onde está, igual a qualquer movimento
contra célula ocupada. A invariante "uma peça por célula" é mantida pela mesma checagem
de ocupação de sempre, não por um resolvedor de conflito. Isso é deliberado: deixa o undo
"travar" de forma previsível perto de peças commitadas, que é a mecânica a explorar.

> Nota: hoje o undo bloqueia (não empurra) ao esbarrar numa peça fora do histórico. Se um
> dia quisermos undo com semântica de movimento COMPLETA (empurrar a verde ao voltar),
> isso é uma extensão — não está implementado.

## Mudança 3 — Gatilho no `MovementSystem`

Depois de resolver o passo (movimento + `Gravity.Apply` + `PressurePlateSystem.Resolve`)
e dar `History.Push(record)`, varrer os movers; pra cada peça sólida (player ou
caixa) cuja célula final tem um `TimelessBase`, chamar `History.Forget(peça)`.
Commit total daquela peça, incluindo o próprio passo que a levou até a placa.

## Consequência registrada

Se num mesmo passo o player empurra uma caixa **e** pisa na placa, o Z seguinte
volta a caixa mas não o player (só o histórico do player foi expurgado). É o que
"apaga só daquela peça" implica — comportamento aceito.

## Mudança 4 — Toggle é estado derivado, nunca no histórico

A solidez dos blocos `Toggle` (controlados por placa de pressão) deixa de ser gravada no
histórico. Antes, `PressurePlateSystem.Resolve` empilhava o estado anterior do toggle no
`record`, e o Restart também o capturava no snapshot — isso criava fonte de verdade dupla
(solidez no histórico vs. derivável das placas) e desincronizava: dar undo de um passo sobre
uma placa pressionada deixava o toggle "preso" como se ainda estivesse pressionado.

Agora:
- `Resolve` NÃO grava a mudança de solidez do toggle. Só a queda das peças que repousavam
  sobre um bloco que sumiu vai pro `record` (isso é movimento real).
- O Restart não captura toggles no snapshot.
- `History.Undo`, depois de restaurar as posições, chama `Resolve` (com record descartável)
  pra re-derivar os toggles a partir das placas pressionadas no estado restaurado.

Resultado: "a placa estava ativa no passado?" deixa de existir como pergunta — o estado do
toggle é sempre função das posições atuais.

## Fora de escopo

Render e editor da placa (cor, brush, texto) seguem iguais. A descrição textual da
placa no editor pode ser ajustada pra refletir o novo comportamento, mas é
cosmético.
