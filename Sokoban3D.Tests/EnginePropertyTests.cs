using System;
using System.Collections.Generic;
using Arch.Core;
using Sokoban3D.Core;
using Sokoban3D.ECS.Components;
using Sokoban3D.ECS.Systems;
using Sokoban3D.Levels;
using Sokoban3D.Solver;
using Xunit;

namespace Sokoban3D.Tests;

/// <summary>
/// Testes de propriedade do engine: playouts aleatórios (seed fixa, reprodutível) sobre os
/// mapas REAIS, checando invariantes que têm que valer depois de qualquer jogada — mais a
/// caracterização da mecânica atemporal (undo assimétrico), pinada num nível sintético.
/// </summary>
public class EnginePropertyTests
{
    private static readonly (int Dx, int Dz)[] Dirs = { (0, -1), (0, 1), (-1, 0), (1, 0) };

    public static TheoryData<int> MapIds() => SolvabilityOracleTests.MapIds();

    private static Level LoadMap(int id) => new LevelRepository(TestLevels.MapsDir()).Load(id);

    // ----- Invariantes sob playout aleatório -----

    [Theory]
    [MemberData(nameof(MapIds))]
    public void RandomPlayoutKeepsInvariants(int id)
    {
        var (session, movement) = TestLevels.Spawn(LoadMap(id));
        var rng = new Random(1234 + id);

        for (int i = 0; i < 200; i++)
        {
            var (dx, dz) = Dirs[rng.Next(Dirs.Length)];
            movement.Step(session, dx, dz);

            AssertGridConsistent(session);
            AssertGravitySettled(session);

            // De vez em quando um undo no meio do passeio; a consistência do grid tem que
            // seguir valendo (assentamento pós-undo NÃO é invariante: o engine re-deriva
            // placas, mas deliberadamente não re-assenta — o reverso devolve posições).
            if (rng.Next(8) == 0)
            {
                movement.Undo(session);
                AssertGridConsistent(session);
            }
        }
    }

    /// <summary>
    /// O contrato central do repo: a ocupação do grid e as entities <see cref="Solid"/> são a
    /// MESMA informação. Cada sólido está registrado em TODAS as células do seu footprint
    /// (a BigBox ocupa duas), e não há célula ocupada órfã (contagens iguais) — o que também
    /// prova que não há dois sólidos na mesma célula.
    /// </summary>
    private static void AssertGridConsistent(GameWorld session)
    {
        var solids = new List<(Entity E, GridPosition P)>();
        var query = new QueryDescription().WithAll<Solid, GridPosition>();
        session.World.Query(in query, (Entity e, ref GridPosition p) => solids.Add((e, p)));

        int footprintCells = 0;
        foreach (var (e, p) in solids)
        {
            foreach (var (dx, dz) in session.FootprintOffsets(e))
            {
                footprintCells++;
                var occupant = session.Grid.Occupant(p.X + dx, p.Y, p.Z + dz);
                Assert.True(occupant is not null && occupant.Value == e,
                    $"Entity sólida com footprint em ({p.X + dx},{p.Y},{p.Z + dz}) não é a ocupante da célula no grid.");
            }
        }

        int occupied = 0;
        for (int x = 0; x < session.Grid.Width; x++)
            for (int y = 0; y < session.Grid.Height; y++)
                for (int z = 0; z < session.Grid.Depth; z++)
                    if (session.Grid.Occupant(x, y, z) is not null)
                        occupied++;

        Assert.Equal(footprintCells, occupied);
    }

    /// <summary>Depois de um turno o mundo está assentado: rodar a gravidade de novo é no-op.</summary>
    private static void AssertGravitySettled(GameWorld session)
    {
        var before = SnapshotMovers(session);
        Gravity.Settle(session);
        var after = SnapshotMovers(session);

        foreach (var (e, pos) in before)
            Assert.True(pos.Equals(after[e]),
                $"Peça flutuando pós-turno: Settle extra moveu ({pos.X},{pos.Y},{pos.Z}) → ({after[e].X},{after[e].Y},{after[e].Z}).");
    }

    private static Dictionary<Entity, GridPosition> SnapshotMovers(GameWorld session)
    {
        var snap = new Dictionary<Entity, GridPosition>();
        var query = new QueryDescription().WithAll<GridPosition, SpawnPosition>();
        session.World.Query(in query, (Entity e, ref GridPosition p) => snap[e] = p);
        return snap;
    }

    // ----- Undo como inverso exato (longe de atemporal/verde/frágil) -----

    [Theory]
    [MemberData(nameof(MapIds))]
    public void UndoIsExactInverseWithoutTimelessElements(int id)
    {
        var level = LoadMap(id);

        // Onde o undo é MECÂNICA (assimétrico de propósito), o inverso exato não vale — esses
        // ficam pra caracterização abaixo. Frágil fica de fora por prudência (quebra no meio
        // do turno).
        if (level.TimelessBaseSpawns.Count > 0)
            return;
        if (level.BoxSpawns.Exists(b => b.Type is BoxType.Permanent or BoxType.Fragile))
            return;

        var sim = new SolverSim(level, trackStacks: true);
        var rng = new Random(99 + id);
        var actions = SolverActions.Directions;

        for (int i = 0; i < 150; i++)
        {
            var before = sim.Capture();
            sim.Apply(actions[rng.Next(actions.Length)]);
            var after = sim.Capture();

            if (after.Equals(before))
                continue; // jogada nula: nada registrado, nada a desfazer

            sim.Apply(SolverAction.Undo);
            Assert.True(sim.Capture().Equals(before),
                $"Undo não devolveu o estado anterior (iteração {i}, level_{id}).");
        }
    }

    // ----- Caracterização: undo assimétrico da base atemporal -----

    /// <summary>
    /// Pina a mecânica atemporal: quem termina o turno sobre a TimelessBase perde o passado —
    /// undos posteriores revertem o resto do mundo, mas a peça fica commitada onde a base a
    /// pegou. Corredor: P(1), base(2), caixa(4). Três passos → três undos: a caixa volta ao
    /// spawn, o player NÃO volta a 1 — pára em 2 (onde a base o commitou), e o histórico esgota.
    /// </summary>
    [Fact]
    public void TimelessBaseCommitsPieceAgainstUndo()
    {
        var level = TestLevels.Corridor(7);
        level.PlayerSpawns.Add((1, 1, 1));
        level.TimelessBaseSpawns.Add((2, 1, 1));
        level.BoxSpawns.Add((4, 1, 1, BoxType.Medium));

        var (session, movement) = TestLevels.Spawn(level);
        var player = session.Spatial.First<Player>().Value;

        movement.Step(session, 1, 0); // P 1→2 (termina na base: pilha do P esvaziada)
        movement.Step(session, 1, 0); // P 2→3
        movement.Step(session, 1, 0); // P 3→4, caixa 4→5

        Assert.True(movement.Undo(session)); // desfaz o 3º turno: P→3, caixa→4
        Assert.True(movement.Undo(session)); // desfaz o 2º: P→2
        Assert.True(movement.Undo(session)); // só a caixa tem passado (inércia): P fica

        var p = session.World.Get<GridPosition>(player);
        Assert.Equal((2, 1, 1), (p.X, p.Y, p.Z)); // commitado pela base — NÃO voltou a 1

        Assert.False(movement.Undo(session)); // histórico esgotado
    }

    // ----- Restart devolve os spawns -----

    [Theory]
    [MemberData(nameof(MapIds))]
    public void RestartReturnsEveryPieceToSpawn(int id)
    {
        var (session, movement) = TestLevels.Spawn(LoadMap(id));
        var rng = new Random(7 + id);

        for (int i = 0; i < 60; i++)
        {
            var (dx, dz) = Dirs[rng.Next(Dirs.Length)];
            movement.Step(session, dx, dz);
        }

        new LevelManager().Restart(session);

        Assert.False(session.PlayerFell);
        var query = new QueryDescription().WithAll<GridPosition, SpawnPosition>();
        session.World.Query(in query, (Entity e, ref GridPosition p, ref SpawnPosition s) =>
        {
            Assert.True(p.X == s.X && p.Y == s.Y && p.Z == s.Z,
                $"Peça fora do spawn após restart: ({p.X},{p.Y},{p.Z}) ≠ ({s.X},{s.Y},{s.Z}).");
            Assert.True(session.World.Has<Solid>(e), "Peça não re-solidificada após restart.");
        });
        AssertGridConsistent(session);
    }
}
