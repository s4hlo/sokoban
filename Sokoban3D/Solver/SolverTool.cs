using System.Collections.Generic;
using Sokoban3D.Core;
using Sokoban3D.ECS.Systems;
using Sokoban3D.Levels;
using Serilog;

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

        // Orçamento próprio do P, mais folgado que o default (que o oráculo de testes usa):
        // aqui é interativo e vale esperar um pouco mais por uma resposta.
        var limits = new SolverLimits { MaxNodes = 2_000_000, MaxTime = System.TimeSpan.FromSeconds(30) };
        try
        {
            var result = PuzzleSolver.Solve(session.CurrentLevel, limits);
            if (result.NoObjective)
                Status = "Sem objetivo neste nível (hub) — nada a resolver.";
            else if (result.Solvable)
            {
                _path = result.Path;
                Status = $"Resolvido em {_path.Count} passos ({result.NodesExplored} nós, {result.Elapsed.TotalMilliseconds:F0} ms) — executando...";
            }
            else if (result.LimitHit)
                Status = $"Inconclusivo: limite da busca estourado ({result.NodesExplored} nós em {result.Elapsed.TotalSeconds:F1} s). [C] toca o certificado, se houver.";
            else
                Status = $"INSOLÚVEL — espaço esgotado ({result.NodesExplored} nós).";
        }
        catch (System.Exception e)
        {
            // O tier macro classifica células "inertes" sem rodar o engine de verdade (ver
            // SolverReach.IsInert); se um dia discordar do replay real, ReplayMacro detona em
            // vez de devolver um caminho errado. Aqui vira status de erro em vez de derrubar o
            // processo inteiro por causa de um dev tool.
            Status = $"Busca falhou internamente: {e.Message}";
            Log.Warning(e, "PuzzleSolver.Solve falhou pro level {Id}", session.LevelId);
        }
    }

    /// <summary>
    /// Entra no modo playback tocando o CERTIFICADO gravado (Solutions/level_N.moves) em vez de
    /// buscar — tecla C. Mesmo reset e mesma execução do modo busca; o certificado é validado
    /// por replay headless antes, pra o HUD acusar um desatualizado em vez de tocar algo que
    /// não conclui.
    /// </summary>
    public void EnterCertificate(GameWorld session)
    {
        _path = null;
        _next = 0;
        _cooldown = StepInterval;

        if (session.CurrentLevel is null)
        {
            Status = "Sem receita de nível na sessão — nada a tocar.";
            return;
        }

        _levels.FullReset(session);

        try
        {
            if (!SolutionStore.TryLoad(session.LevelId, out var moves))
            {
                Status = "Sem certificado gravado pra este nível — vença uma vez que ele grava sozinho.";
                return;
            }

            if (!SolutionStore.Solves(session.CurrentLevel, moves))
            {
                Status = "Certificado desatualizado (não resolve mais o nível) — vença uma vez pra regravar.";
                return;
            }

            _path = moves;
            Status = $"Certificado: {_path.Count} passos — executando...";
        }
        catch (System.Exception e)
        {
            Status = $"Certificado ilegível: {e.Message}";
        }
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

        // Mode-agnóstico: serve tanto pra solução achada pela busca (P) quanto pro certificado (C).
        Status = _next >= _path.Count
            ? $"Concluído: {_path.Count} passos. [P] volta ao jogo."
            : $"Executando passo {_next}/{_path.Count} ({_path[_next - 1]}).";
    }

    /// <summary>Sai do modo: descarta o playback (o estado do mundo fica onde estiver; R/F resolvem).</summary>
    public void Exit(GameWorld session)
    {
        _path = null;
        _next = 0;
    }
}
