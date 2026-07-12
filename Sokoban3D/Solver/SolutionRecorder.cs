using System;
using System.Collections.Generic;
using Sokoban3D.Core;
using Serilog;

namespace Sokoban3D.Solver;

/// <summary>
/// Gravador de certificado de solução (dev tool, par do <see cref="SolverTool"/>): acumula as
/// ações do player desde o início da tentativa e, quando ele pisa no objetivo, salva a sequência
/// via <see cref="SolutionStore"/> — o formato que o oráculo de solvabilidade e o P replayam
/// quando a busca do solver é inconclusiva (níveis grandes/hostis à busca).
///
/// O log é POR NÍVEL: <see cref="Track"/> aponta o gravador pro nível ativo a cada troca de
/// sessão (LevelChanged). Suspender (T) e voltar, ou saltar de nível e voltar, preservam a sessão
/// do engine — o log tem que acompanhar, senão uma vitória depois de retomar salva só o rabo da
/// tentativa (dessincroniza do estado de receita). Só reseta quando a tentativa de fato recomeça:
/// nível novo, R, F, ou entrada no solver (que também reseta o mundo pro estado de receita).
///
/// Política de gravação: um certificado existente que AINDA RESOLVE o nível fica intocado
/// (curadoria vale mais que uma vitória casual de 400 passos); ausente ou desatualizado (nível
/// editado), a vitória de agora vira o certificado novo — mexeu no mapa, é só vencer de novo. O
/// log é sempre reconferido contra o engine (replay real) antes de virar certificado — nunca
/// escreve uma sequência que não prova nada.
/// </summary>
public sealed class SolutionRecorder
{
    private readonly Dictionary<int, List<SolverAction>> _logs = new();
    private int _activeLevelId = -1;

    /// <summary>Aponta o gravador pro nível ativo (chamado a cada LevelChanged do navigator).
    /// Nível nunca visto (ou já resetado) começa com log vazio; revisitado retoma o log de onde
    /// parou.</summary>
    public void Track(int levelId) => _activeLevelId = levelId;

    /// <summary>Descarta o log do nível informado: começa uma tentativa nova porque o MUNDO dele
    /// resetou de verdade (R, F, ou entrada no solver) — não basta trocar de sessão ativa.</summary>
    public void Reset(int levelId) => _logs.Remove(levelId);

    private List<SolverAction> CurrentLog()
    {
        if (!_logs.TryGetValue(_activeLevelId, out var log))
            _logs[_activeLevelId] = log = new List<SolverAction>();
        return log;
    }

    /// <summary>Registra um passo do player na direção (dx,dz) — a convenção do teclado.</summary>
    public void RecordStep(int dx, int dz)
    {
        SolverAction? action = (dx, dz) switch
        {
            (0, -1) => SolverAction.Up,
            (0, 1) => SolverAction.Down,
            (-1, 0) => SolverAction.Left,
            (1, 0) => SolverAction.Right,
            _ => null,
        };
        if (action is { } a)
            CurrentLog().Add(a);
    }

    /// <summary>Registra um undo (Z) que de fato reverteu um turno.</summary>
    public void RecordUndo() => CurrentLog().Add(SolverAction.Undo);

    /// <summary>
    /// Player pisou no objetivo: grava a tentativa como certificado do nível, a menos que o
    /// certificado existente ainda resolva ou o próprio log não bata com o engine (dessincronia
    /// entre R/Z fora de ordem, sessão retomada de um jeito que o log não capturou etc.). A
    /// tentativa deste nível termina aqui de todo jeito — salva ou não, o log é descartado (o
    /// nível concluído sempre recomeça do zero na próxima visita). Nunca explode — é dev tool;
    /// sem repositório por perto (build distribuído), só ignora.
    /// </summary>
    public void SaveOnWin(GameWorld session)
    {
        var moves = CurrentLog();
        try
        {
            if (session.LevelId < 0 || session.CurrentLevel is null || moves.Count == 0)
                return;

            // O log tem que provar a própria vitória replayando do estado de receita — senão a
            // gravação corromperia um certificado com uma sequência que não reproduz.
            if (!SolutionStore.Solves(session.CurrentLevel, moves))
            {
                Log.Warning("Log da tentativa do level {Id} não bate com o engine — não vira certificado.", session.LevelId);
                return;
            }

            // Existente e ainda válido fica; ilegível ou desatualizado é tratado como velho.
            bool keepExisting = false;
            try
            {
                keepExisting = SolutionStore.TryLoad(session.LevelId, out var existing)
                    && SolutionStore.Solves(session.CurrentLevel, existing);
            }
            catch (Exception)
            {
                // Certificado ilegível: a vitória de agora o substitui.
            }
            if (keepExisting)
                return;

            SolutionStore.Save(session.LevelId, moves);
            Log.Information("Certificado do level {Id} gravado ({Count} passos)", session.LevelId, moves.Count);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Falha ao gravar certificado de solução do level {Id}", session.LevelId);
        }
        finally
        {
            _logs.Remove(session.LevelId);
        }
    }
}
