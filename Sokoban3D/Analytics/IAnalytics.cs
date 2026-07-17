namespace Sokoban3D.Analytics;

/// <summary>
/// Coletor de telemetria de jogo — abstração sobre o backend concreto (GameAnalytics ou no-op).
/// O jogo fala em eventos de DOMÍNIO (nível começou / concluído / abandonado); traduzir isso pro
/// vocabulário do backend (progression/design events) é responsabilidade da implementação. Assim
/// o resto do código não conhece o GameAnalytics, e trocar/desligar o backend é uma linha.
///
/// Contrato inviolável: nenhum método aqui pode LANÇAR nem BLOQUEAR o loop do jogo. Telemetria é
/// acessória — se o backend falhar (rede, chave inválida), o jogo segue como se nada fosse. Cada
/// implementação engole as próprias exceções.
/// </summary>
public interface IAnalytics
{
    /// <summary>Tentativa NOVA num nível: primeira entrada ou recomeço do zero (reset total F).</summary>
    void LevelStarted(int levelId, string levelName);

    /// <summary>
    /// Nível concluído pela meta. <paramref name="seconds"/> é o tempo ATIVO da tentativa (não
    /// conta o tempo em que o nível ficou suspenso enquanto o player resolvia um filho/outro nível).
    /// </summary>
    void LevelCompleted(int levelId, string levelName, int moves, int undos, int restarts, double seconds);

    /// <summary>
    /// Tentativa abandonada sem concluir. <paramref name="reason"/> distingue o motivo (reset total
    /// vs. saída do jogo) — é o sinal de "drop" por nível pro funil de retenção.
    /// </summary>
    void LevelAbandoned(int levelId, string levelName, string reason, int moves, int undos, int restarts, double seconds);

    /// <summary>
    /// O player navegou de um nível para outro (troca de sessão ativa) — inclui o hub e níveis já
    /// resolvidos, ao contrário do rastreio de tentativa. É a TRILHA de navegação: dela sai a ordem
    /// em que o player escolhe os níveis e a matriz de transições pra desenhar o hub. <c>fromLevelId
    /// = -1</c> é o início da sessão (primeira entrada, sem nível anterior).
    /// </summary>
    void LevelVisited(int fromLevelId, int toLevelId);

    /// <summary>Fim da aplicação: encerra a sessão e descarrega a fila de eventos pendentes.</summary>
    void Shutdown();
}
