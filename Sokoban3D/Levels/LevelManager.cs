using System;
using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;
using Sokoban3D.ECS.Systems;

namespace Sokoban3D.Levels;

/// <summary>
/// Define um nível do jogo
/// </summary>
public class Level
{
    public int Width;
    public int Height;
    public int Depth;
    public string Name;

    /// <summary>Identidade do nível pra rastrear conclusão global. -1 = sem id (default).</summary>
    public int Id = -1;

    // Dados: posições de player, caixas, objetivos, inimigos
    public List<(int X, int Y, int Z)> PlayerSpawns = new();
    public List<(int X, int Y, int Z, BoxType Type)> BoxSpawns = new();
    public List<(int X, int Y, int Z)> ObjectiveSpawns = new();
    public List<(int X, int Y, int Z)> EnemySpawns = new();

    // Obstáculos: blocos sólidos fixos que formam o terreno, com o tipo (comum, sticky).
    // Empilhados (vários Y na mesma coluna) viram paredes; uma camada inteira em y=0 é o
    // piso onde as peças andam.
    public List<(int X, int Y, int Z, ObstacleType Type)> ObstacleSpawns = new();

    /// <summary>
    /// Preenche uma camada inteira (todo X/Z) de obstáculos comuns na altura <paramref name="y"/>.
    /// É o piso padrão: sem buracos, ninguém cai. Remover obstáculos depois abre buracos.
    /// </summary>
    public void FillFloor(int y = 0)
    {
        for (int x = 0; x < Width; x++)
            for (int z = 0; z < Depth; z++)
                ObstacleSpawns.Add((x, y, z, ObstacleType.Normal));
    }

    /// <summary>
    /// Abre um buraco: remove o obstáculo da coluna (x,z) na altura <paramref name="y"/>.
    /// O player que pisar ali sem nada sólido embaixo cai.
    /// </summary>
    public void DigHole(int x, int z, int y = 0)
    {
        ObstacleSpawns.RemoveAll(o => o.X == x && o.Y == y && o.Z == z);
    }

    // Portais (entradas para níveis filhos): cada um leva ao nível LevelIndex; Completed muda a cor.
    // Qualquer nível pode ter portais — é assim que a árvore se ramifica.
    public List<(int X, int Y, int Z, int LevelIndex, bool Completed)> PortalSpawns = new();

    // Placas de pressão e blocos controláveis. Group liga as placas aos toggles de mesmo número.
    // Não ocupam o grid (placas); os toggles ocupam quando sólidos. SolidByDefault: estado em repouso.
    public List<(int X, int Y, int Z, int Group)> PlateSpawns = new();
    // Threshold: quantas placas do grupo precisam estar pressionadas pra acionar (1 = OR; total do grupo = AND).
    public List<(int X, int Y, int Z, int Group, bool SolidByDefault, int Threshold)> ToggleSpawns = new();

    // Bases atemporais: marcadores que não ocupam o grid e congelam o undo de quem está em cima.
    public List<(int X, int Y, int Z)> TimelessBaseSpawns = new();

    // Trilhos: marcadores que não ocupam o grid e limitam as direções de SAÍDA da caixa em cima
    // (a entrada é livre; o undo ignora). O tipo define as saídas (ver RailRules).
    public List<(int X, int Y, int Z, RailType Type)> RailSpawns = new();

    // Caixas portal: pares ligados por Group. Quem tenta entrar numa é teleportado pro lado
    // oposto da parceira. Ocupam o grid como caixa comum (caem, têm undo).
    public List<(int X, int Y, int Z, int Group)> PortalBoxSpawns = new();

    // Caixas grandes (1x2/2x1): ocupam a célula (X,Y,Z) [âncora] e a adjacente no Axis. Sempre
    // Light — ver BigBox. Caem e têm undo iguais a uma caixa comum, mas com footprint de 2 células.
    public List<(int X, int Y, int Z, BigBoxAxis Axis)> BigBoxSpawns = new();

    /// <summary>
    /// Cópia profunda da receita. O editor edita um clone do nível ativo, então o estado de
    /// jogo (caixas já movidas) não interfere no design, e vice-versa.
    /// </summary>
    public Level Clone()
    {
        return new Level
        {
            Width = Width,
            Height = Height,
            Depth = Depth,
            Name = Name,
            Id = Id,
            PlayerSpawns = new(PlayerSpawns),
            BoxSpawns = new(BoxSpawns),
            ObjectiveSpawns = new(ObjectiveSpawns),
            EnemySpawns = new(EnemySpawns),
            ObstacleSpawns = new(ObstacleSpawns),
            PortalSpawns = new(PortalSpawns),
            PlateSpawns = new(PlateSpawns),
            ToggleSpawns = new(ToggleSpawns),
            TimelessBaseSpawns = new(TimelessBaseSpawns),
            RailSpawns = new(RailSpawns),
            PortalBoxSpawns = new(PortalBoxSpawns),
            BigBoxSpawns = new(BigBoxSpawns),
        };
    }
}

/// <summary>
/// Monta níveis dentro de uma sessão (GameWorld). É um serviço sem estado: cada método
/// recebe a sessão sobre a qual opera, pra que a mesma instância sirva qualquer sessão
/// da pilha de navegação.
/// </summary>
public class LevelManager
{
    public void LoadLevel(GameWorld session, Level level)
    {
        session.CurrentLevel = level;
        session.LevelId = level.Id;

        // Remove entidades de um nível anterior (importante pra troca de nível não duplicar).
        DestroyAllEntities(session);

        // Histórico zerado: um nível recém-carregado não tem nada pra desfazer. No reset total (F)
        // é isso que apaga o undo acumulado da sessão.
        session.History.Clear();

        // Ajusta o grid às dimensões do nível (realoca o mapa de ocupação zerado).
        session.Grid.Resize(level.Width, level.Height, level.Depth);
        session.PlayerFell = false;

        // Spawn obstáculos (terreno). São estáticos: ocupam o grid, mas não têm
        // RenderPosition — o render os desenha direto da célula lógica.
        foreach (var (x, y, z, type) in level.ObstacleSpawns)
        {
            var entity = session.World.Create(
                new GridPosition(x, y, z),
                new Obstacle { Type = type },
                new Solid()
            );
            session.Occupy(entity);
        }

        // Spawn player
        foreach (var (x, y, z) in level.PlayerSpawns)
        {
            var entity = session.World.Create(
                new GridPosition(x, y, z),
                new Player { Speed = 1f },
                Facing.Default,
                new RenderFacing { Yaw = Facing.Default.Yaw },
                new SpawnPosition(x, y, z),
                new RenderPosition(GridView.ToWorld(session.Grid, x, y, z, GridView.PieceRise)),
                new Solid()
            );
            session.Occupy(entity);
        }

        // Spawn boxes
        foreach (var (x, y, z, type) in level.BoxSpawns)
        {
            var entity = session.World.Create(
                new GridPosition(x, y, z),
                new Box { Type = type },
                new SpawnPosition(x, y, z),
                new RenderPosition(GridView.ToWorld(session.Grid, x, y, z, GridView.PieceRise)),
                new Solid()
            );
            session.Occupy(entity);
        }

        // Spawn caixas portal: caixas comuns (ocupam o grid, caem, têm undo) com o pareamento
        // PortalBox. O teleporte mora no MovementSystem; aqui é só uma caixa do tipo Portal.
        foreach (var (x, y, z, group) in level.PortalBoxSpawns)
        {
            var entity = session.World.Create(
                new GridPosition(x, y, z),
                new Box { Type = BoxType.Portal },
                new PortalBox { Group = group },
                new SpawnPosition(x, y, z),
                new RenderPosition(GridView.ToWorld(session.Grid, x, y, z, GridView.PieceRise)),
                new Solid()
            );
            session.Occupy(entity);
        }

        // Spawn caixas grandes: caixa comum (Light) com o marcador BigBox, que faz session.Occupy
        // (footprint-aware) ocupar a âncora e a célula extra do Axis.
        foreach (var (x, y, z, axis) in level.BigBoxSpawns)
        {
            var entity = session.World.Create(
                new GridPosition(x, y, z),
                new Box { Type = BoxType.Light },
                new BigBox { Axis = axis },
                new SpawnPosition(x, y, z),
                new RenderPosition(GridView.ToWorld(session.Grid, x, y, z, GridView.PieceRise)),
                new Solid()
            );
            session.Occupy(entity);
        }

        // Spawn objectives
        int objId = 0;
        foreach (var (x, y, z) in level.ObjectiveSpawns)
        {
            var entity = session.World.Create(
                new GridPosition(x, y, z),
                new Objective { Id = objId++ },
                new CellMarker()
            );
            // Objetivos não ocupam espaço
        }

        // Spawn enemies
        foreach (var (x, y, z) in level.EnemySpawns)
        {
            var entity = session.World.Create(
                new GridPosition(x, y, z),
                new Enemy { Speed = 0.5f },
                new SpawnPosition(x, y, z),
                new Solid()
            );
            session.Occupy(entity);
        }

        // Spawn portais (entradas para níveis filhos; qualquer nível pode ter). Não ocupam
        // o grid: o player precisa poder pisar em cima pra entrar.
        foreach (var (x, y, z, levelIndex, completed) in level.PortalSpawns)
        {
            session.World.Create(
                new GridPosition(x, y, z),
                new LevelPortal { LevelIndex = levelIndex, Completed = completed },
                new CellMarker()
            );
        }

        // Spawn placas de pressão. Não ocupam o grid (o player/caixa pisa em cima pra acionar).
        foreach (var (x, y, z, group) in level.PlateSpawns)
        {
            session.World.Create(
                new GridPosition(x, y, z),
                new PressurePlate { Group = group },
                new CellMarker()
            );
        }

        // Spawn bases atemporais. Marcador (não ocupa o grid): a peça pisa em cima e o histórico
        // dela é expurgado — fica commitada ali (ver History.Forget / TimelessBase).
        foreach (var (x, y, z) in level.TimelessBaseSpawns)
        {
            session.World.Create(
                new GridPosition(x, y, z),
                new TimelessBase(),
                new CellMarker()
            );
        }

        // Spawn trilhos. Marcador (não ocupa o grid): a caixa em cima só sai nas direções do
        // tipo (ver Core.Rails); a entrada é livre e o undo ignora.
        foreach (var (x, y, z, type) in level.RailSpawns)
        {
            session.World.Create(
                new GridPosition(x, y, z),
                new Rail { Type = type },
                new CellMarker()
            );
        }

        // Spawn blocos toggle no estado de repouso: sólidos (ocupam o grid, igual a obstáculo)
        // só se SolidByDefault. Não se movem — sem SpawnPosition/RenderPosition.
        foreach (var (x, y, z, group, solidByDefault, threshold) in level.ToggleSpawns)
        {
            var entity = solidByDefault
                ? session.World.Create(
                    new GridPosition(x, y, z),
                    new Toggle { Group = group, SolidByDefault = true, Threshold = threshold },
                    new Solid())
                : session.World.Create(
                    new GridPosition(x, y, z),
                    new Toggle { Group = group, SolidByDefault = false, Threshold = threshold });

            if (solidByDefault)
                session.Occupy(entity);
        }

        // Estado inicial das placas: uma peça pode já nascer sobre uma placa (inverte o bloco).
        PressurePlateSystem.Resolve(session);
    }

    /// <summary>
    /// Reset total (tecla F): joga tudo fora e remonta o nível do zero a partir da receita ativa —
    /// destrói as entidades, zera o grid e LIMPA o histórico de undo. Diferente do <see cref="Restart"/>
    /// (R), que só reposiciona as peças existentes pra manter os handles válidos e deixar o próprio R
    /// ser desfeito; aqui não sobra nada pra desfazer, o nível volta ao estado de primeira visita.
    /// </summary>
    public void FullReset(GameWorld session)
    {
        if (session.CurrentLevel == null)
            return;

        LoadLevel(session, session.CurrentLevel);
    }

    /// <summary>
    /// Volta todas as peças à posição inicial (R). Reposiciona as entidades existentes em vez de
    /// destruir/recriar — os handles de Entity seguem válidos, então o reverso (Z) desfaz o próprio
    /// restart, gravado como um turno no histórico antes de reposicionar.
    /// </summary>
    public void Restart(GameWorld session)
    {
        if (session.CurrentLevel == null)
            return;

        var history = session.History;
        var query = new QueryDescription().WithAll<GridPosition, SpawnPosition>();

        // 1) Grava o restart como um movimento (delta = spawn - atual). Caixa quebrada vira
        //    SolidChange.Gained, pra o reverso voltar a quebrá-la. A verde não entra (só o R a move);
        //    toggles também não (solidez derivada das placas).
        session.World.Query(in query, (Entity e, ref GridPosition pos, ref SpawnPosition spawn) =>
        {
            if (session.World.Has<Box>(e) && session.World.Get<Box>(e).Type == BoxType.Permanent)
                return;

            var change = session.World.Has<Solid>(e) ? SolidChange.None : SolidChange.Gained;
            Facing? face = session.World.Has<Facing>(e) ? session.World.Get<Facing>(e) : null;
            history.Record(e, new Move(spawn.X - pos.X, spawn.Y - pos.Y, spawn.Z - pos.Z, change, face));
        });

        // 2) Reposiciona cada peça pro spawn e reconstrói o grid do zero. O player deixa
        //    de estar "caído".
        session.Grid.Clear();
        session.PlayerFell = false;

        // Obstáculos não têm SpawnPosition (não se movem), então o Clear acima apagaria a
        // ocupação deles. Remarca o terreno antes de repor as peças.
        var obstacles = new QueryDescription().WithAll<Obstacle, GridPosition>();
        session.World.Query(in obstacles, (Entity e, ref GridPosition pos) =>
            session.Grid.Place(pos.X, pos.Y, pos.Z, e));

        // Caixas que tinham quebrado precisam reocupar o grid (readicionar Solid). Como Add é
        // mudança estrutural — proibida durante a iteração de uma query —, coleta-se aqui e
        // aplica-se depois.
        var toResolidify = new List<Entity>();
        session.World.Query(in query, (Entity e, ref GridPosition pos, ref SpawnPosition spawn) =>
        {
            pos = new GridPosition(spawn.X, spawn.Y, spawn.Z);

            if (!session.World.Has<Solid>(e))
                toResolidify.Add(e);

            foreach (var (dx, dz) in session.FootprintOffsets(e))
                session.Grid.Place(spawn.X + dx, spawn.Y, spawn.Z + dz, e);
        });

        foreach (var e in toResolidify)
            session.World.Add(e, new Solid());

        // O olhar do player volta ao default do spawn. O olhar pré-restart foi gravado no
        // turno acima, então o reverso (Z) do restart também o restaura.
        var players = new QueryDescription().WithAll<Player, Facing>();
        session.World.Query(in players, (ref Facing f) => f = Facing.Default);

        // Blocos toggle voltam ao repouso (o Clear acima zerou a ocupação deles) e re-derivam
        // pelas placas — uma peça pode ter voltado a um spawn que fica sobre uma placa.
        PressurePlateSystem.Reset(session);
    }

    private static void DestroyAllEntities(GameWorld session)
    {
        var toDestroy = new List<Entity>();
        var query = new QueryDescription().WithAll<GridPosition>();
        session.World.Query(in query, (Entity entity) => toDestroy.Add(entity));

        foreach (var entity in toDestroy)
            session.World.Destroy(entity);
    }
}
