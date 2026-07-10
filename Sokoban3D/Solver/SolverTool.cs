using System.Collections.Generic;
using Sokoban3D.Core;
using Sokoban3D.ECS.Systems;
using Sokoban3D.Levels;

namespace Sokoban3D.Solver;

/// <summary>
/// Dev tool de playback (como o <c>LevelEditor</c> é pro design): resolve o nível ativo e
/// EXECUTA a solução dentro do jogo, uma ação por batida, injetando cada uma no MESMO
/// <see cref="MovementSystem.Step"/>/<see cref="MovementSystem.Undo"/> que o teclado usa — o
/// replay não tem como dessincronizar do que o solver provou. A apresentação (HUD) fica no
/// <c>SolverRenderer</c>; a fiação (tecla P, pausar o input do player) no <c>Game1</c>.
/// </summary>
public class SolverTool
{
    // Cadência do playback: uma ação a cada tanto — folga pra animação de deslize (~18 de
    // smoothing) praticamente assentar entre passos.
    private const float StepInterval = 0.25f;

    private readonly LevelManager _levels;
    private readonly MovementSystem _movement;

    private List<SolverAction> _path;
    private int _next;
    private float _cooldown;

    /// <summary>Linha de status pro HUD ("Resolvido em N — passo i/N", "Insolúvel...", etc.).</summary>
    public string Status { get; private set; } = "";

    /// <summary>True quando não há (mais) nada a executar — sem solução ou playback concluído.</summary>
    public bool Finished => _path is null || _next >= _path.Count;

    public SolverTool(LevelManager levels, MovementSystem movement)
    {
        _levels = levels;
        _movement = movement;
    }

    /// <summary>
    /// Entra no modo solver: reseta o nível pro estado de receita e resolve. FullReset, NÃO
    /// Restart — o restart é gravado no histórico e dessincronizaria os Undo do caminho no
    /// tier atemporal; o FullReset zera o histórico, exatamente o estado que o solver assume.
    /// </summary>
    public void Enter(GameWorld session)
    {
        _path = null;
        _next = 0;
        _cooldown = StepInterval;

        if (session.CurrentLevel is null)
        {
            Status = "Sem receita de nível na sessão — nada a resolver.";
            return;
        }

        _levels.FullReset(session);

        var result = PuzzleSolver.Solve(session.CurrentLevel);
        if (result.NoObjective)
            Status = "Sem objetivo neste nível (hub) — nada a resolver.";
        else if (result.Solvable)
        {
            _path = result.Path;
            Status = $"Resolvido em {_path.Count} passos ({result.NodesExplored} nós, {result.Elapsed.TotalMilliseconds:F0} ms) — executando...";
        }
        else if (result.LimitHit)
            Status = $"Inconclusivo: limite da busca estourado ({result.NodesExplored} nós em {result.Elapsed.TotalSeconds:F1} s).";
        else
            Status = $"INSOLÚVEL — espaço esgotado ({result.NodesExplored} nós).";
    }

    /// <summary>
    /// Playback: a cada <see cref="StepInterval"/> injeta a próxima ação da solução no engine.
    /// O chamador mantém animação e placas rodando (é um turno normal do jogo).
    /// </summary>
    public void Update(GameWorld session, float deltaSeconds)
    {
        if (Finished)
            return;

        _cooldown -= deltaSeconds;
        if (_cooldown > 0f)
            return;
        _cooldown = StepInterval;

        var action = _path[_next++];
        if (action == SolverAction.Undo)
        {
            if (_movement.Undo(session))
                _movement.AnimateUndoTeleports(session, session.History.LastReverted);
        }
        else
        {
            var (dx, dz) = SolverActions.Delta(action);
            _movement.Step(session, dx, dz);
        }

        Status = _next >= _path.Count
            ? $"Concluído: {_path.Count} passos. [P] volta ao jogo."
            : $"Resolvido em {_path.Count} passos — passo {_next}/{_path.Count} ({_path[_next - 1]}).";
    }

    /// <summary>Sai do modo: descarta o playback (o estado do mundo fica onde estiver; R/F resolvem).</summary>
    public void Exit(GameWorld session)
    {
        _path = null;
        _next = 0;
    }
}
