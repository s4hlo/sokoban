# Editor de Níveis — Plano de implementação

## Arquivos novos
- `Editor/EditorBrush.cs` — enum `EditorBrush` { Obstacle, Box, Objective, Portal, Player, Eraser }.
- `Editor/LevelEditor.cs` — estado e lógica do editor: cursor, brush, tipo de caixa, alvo de
  portal; `Enter/Exit`, `Update(session, keyboard)`, mutação do `Level`, rebuild via
  `LevelManager.LoadLevel`, save/load/new. Expõe estado pro renderer e um evento
  `GridChanged` (câmera reenquadra).
- `Editor/EditorRenderer.cs` — desenha o cursor (wireframe, sempre visível) e o HUD (SpriteBatch
  + SpriteFont).
- `Levels/LevelSerializer.cs` — DTO + save/load JSON de um `Level`.
- `Content/Hud.spritefont` — descrição de fonte (Consolas 14) p/ o HUD; registrada no
  `Content.mgcb`.

## Mudanças em arquivos existentes
- `Levels/LevelManager.cs` (classe `Level`) — método `Clone()`.
- `Core/CubeRenderer.cs` — `DrawWireframe(pos, scale, color, view, proj)` usando um cubo de
  arestas (8 vértices / 12 linhas) com `BasicEffect` sem iluminação.
- `ECS/Systems/RenderSystem.cs` — ctor passa a receber um `CubeRenderer` (em vez de criá-lo),
  pra Game1 compartilhar a mesma instância com o `EditorRenderer`.
- `Game1.cs` — possui `CubeRenderer`, `LevelEditor`, `EditorRenderer`, `SpriteFont`, flag
  `_editorActive`; roteia `Update`/`Draw` por modo; `Tab` alterna; câmera reenquadra no evento
  `GridChanged`.
- `Content/Content.mgcb` — entrada da fonte.

## Roteiro
1. `Level.Clone()` + `EditorBrush` + `LevelSerializer` (base de dados, sem render).
2. `CubeRenderer.DrawWireframe` + refactor do ctor de `RenderSystem`.
3. SpriteFont (`Hud.spritefont` + `Content.mgcb`).
4. `LevelEditor` (lógica completa).
5. `EditorRenderer` (cursor + HUD).
6. Fiação no `Game1`.
7. `dotnet build` e ajustes.

## Verificação
- `dotnet build` compila (inclui o build da fonte pelo mgcb).
- Teste manual: Tab entra no editor; cursor anda em XYZ; brushes 1/2/3/4/5/0 colocam/apagam;
  caixa cicla tipo; portal ajusta alvo; Shift+setas redimensiona e a câmera reenquadra; F5
  salva (caminho no log) e F9 recarrega; N zera; Tab volta e o nível é jogável na hora.
