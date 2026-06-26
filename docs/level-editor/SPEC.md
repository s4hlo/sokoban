# Editor de Níveis — Spec

## Objetivo
Editor in-game que permite desenhar um nível à mão: posicionar obstáculos (terreno/paredes),
caixas (de qualquer tipo), a meta, spawn do player, inimigos e portais para outros níveis;
mover peças em altura (Y) livremente; redimensionar o grid; e salvar/carregar o resultado.
No fim, alternar de volta pro modo jogo testa o nível imediatamente.

## Princípio de design
O editor **não** reinventa o modelo do jogo. Ele edita um objeto `Level` (o data-bag que já
existe) e a cada mudança re-materializa a sessão via `LevelManager.LoadLevel`. Assim:
- O `Level` é a única fonte de verdade do que está sendo desenhado.
- O render do jogo (`RenderSystem`) já desenha tudo — o editor só acrescenta cursor + HUD.
- Salvar = serializar o `Level`. Testar = voltar pro modo jogo (a sessão já está carregada).

## Modos
O jogo passa a ter dois modos sobre a sessão ativa: **Jogo** e **Editor**, alternados por `Tab`.
No modo Editor os sistemas de jogo (movimento, gravidade, conclusão) ficam pausados.

Ao entrar no editor, clona-se a receita atual (`session.CurrentLevel`) e recarrega-se pristina
(descartando o progresso de jogo daquele nível — o editor mostra o *design*, não o estado de
partida). Ao sair, a receita editada vira o novo `CurrentLevel` e é recarregada, então o jogo
recomeça a partir do design editado.

## Cursor
Uma célula destacada (wireframe colorido pela brush) navegável em 3D, sempre visível (desenhada
por cima da geometria).

Mesmo esquema do movimento no jogo (A/D = X, W/S = Z), com Q/E pra altura.

| Tecla | Ação |
|-------|------|
| A / D | mover cursor em X |
| W / S | mover cursor em Z (W = fundo / −Z) |
| Q / E | mover cursor em Y (baixo / cima) |

## Paleta de brushes
| Tecla | Brush |
|-------|-------|
| 1 | Obstáculo (terreno/parede) |
| 2 | Caixa — repetir cicla o tipo (Light→Medium→Heavy→Fragile→Permanent) |
| 3 | Meta (objetivo) |
| 4 | Portal — alvo ajustável com `[` / `]` |
| 5 | Player (spawn único) |
| 0 | Borracha |

| Tecla | Ação |
|-------|------|
| Espaço | aplica a brush na célula do cursor |
| Delete / Backspace | apaga o conteúdo da célula |
| `[` / `]` | decrementa / incrementa o id-alvo do portal |

### Regras de aplicação
- **Sólidos** (obstáculo, caixa, player, inimigo): aplicar remove qualquer outro sólido na
  célula antes de adicionar (1 sólido por célula). Player é único no nível inteiro.
- **Marcadores** (meta, portal): aplicar remove qualquer marcador na célula antes de adicionar.
- **Borracha**: remove tudo (sólidos + marcadores) na célula.

Colocar peças "no ar" é permitido — a gravidade do jogo as assenta ao testar.

## Redimensionar o grid
Segurando `Shift`:
| Tecla | Ação |
|-------|------|
| Shift+D / Shift+A | largura (X) +/− |
| Shift+S / Shift+W | profundidade (Z) +/− |
| Shift+E / Shift+Q | altura (Y) +/− |

Mínimo 1 por eixo. Ao encolher, peças fora dos novos limites são descartadas; o cursor é
clampeado. A câmera reenquadra automaticamente.

## Persistência
| Tecla | Ação |
|-------|------|
| F5 | salva o nível em `CustomLevels/level_<id>.json` (caminho absoluto logado) |
| F9 | recarrega esse arquivo no editor |
| N | novo nível em branco (piso preenchido + 1 player) no slot atual |

Serialização: JSON legível (System.Text.Json) via DTO dedicado, enums como string.

## HUD
Overlay de texto (SpriteFont) no canto: modo, posição do cursor, dimensões do grid, brush
atual (e tipo de caixa / alvo do portal) e um lembrete das teclas.

## Fora de escopo (v1)
- Seleção/arraste com mouse (a câmera é isométrica fixa; picking fica pra depois).
- Editar a árvore de navegação / catálogo persistente (portais apontam por id numérico).
- Undo dentro do editor.
