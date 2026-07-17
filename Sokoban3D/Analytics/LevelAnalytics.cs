using System.Collections.Generic;
using System.Diagnostics;
using Sokoban3D.Levels;

namespace Sokoban3D.Analytics;

/// <summary>
/// Rastreia uma TENTATIVA por nível e traduz os eventos do loop de jogo (entrar num nível, mover,
/// desfazer, reiniciar, vencer, sair) em eventos de domínio pro <see cref="IAnalytics"/>. É o único
/// objeto de telemetria que o <c>Game1</c> conhece — segue o mesmo padrão do <c>SolutionRecorder</c>:
/// o Game1 cutuca este tracker nos mesmos pontos em que já cutuca o gravador de certificado.
///
/// Modelo de tentativa (casado com a árvore de níveis do <see cref="Core.LevelNavigator"/>):
/// <list type="bullet">
/// <item>Uma tentativa começa na primeira entrada num nível e sobrevive a suspender/retomar —
///   entrar num portal-filho SUSPENDE a do pai (o cronômetro pausa), voltar RETOMA de onde parou.
///   Assim o "tempo pra resolver" do pai não infla com o tempo gasto resolvendo um filho.</item>
/// <item>Movimentos, undos e restarts acumulam pela tentativa inteira (preservados junto da sessão).</item>
/// <item>Concluir pela meta FECHA a tentativa (LevelCompleted). Reset total (F) fecha como abandono
///   e abre uma tentativa nova. Sair do jogo fecha toda tentativa aberta como abandono — o "drop".</item>
/// </list>
/// O nível-raiz da árvore (hub, sem meta) não é rastreado: ele nunca conclui, então geraria só
/// ruído de "100% de drop".
/// </summary>
public sealed class LevelAnalytics
{
    /// <summary>Estado acumulado de uma tentativa em andamento.</summary>
    private sealed class Attempt
    {
        public string Name;
        public int Moves;
        public int Undos;
        public int Restarts;
        // Cronômetro do tempo ATIVO: roda só enquanto o nível é a sessão do topo; pausa ao suspender.
        public readonly Stopwatch Timer = new();
    }

    private const int None = int.MinValue;

    private readonly IAnalytics _sink;

    // Tentativas vivas por id de nível. Espelha o cache de sessões do navigator: um nível suspenso
    // mantém a tentativa (pausada) aqui até vencer, resetar (F) ou o jogo fechar.
    private readonly Dictionary<int, Attempt> _attempts = new();

    // Nível que está cronometrando agora (topo da pilha do navigator). None = nenhum rastreável ativo.
    private int _active = None;

    // Nível ANTERIOR na trilha de navegação (id real, inclui o hub). -1 = início da sessão, sem
    // anterior. Separado de _active porque a trilha registra TODA transição — inclusive hub e níveis
    // já resolvidos —, não só as tentativas de puzzle.
    private int _previous = -1;

    public LevelAnalytics(IAnalytics sink) => _sink = sink;

    // A raiz (hub) e ids inválidos (-1) não são tentativas de puzzle — ficam de fora da telemetria.
    private static bool Trackable(int levelId) => levelId >= 0 && levelId != LevelCatalog.RootId;

    /// <summary>
    /// A sessão ativa mudou (assinado no <c>LevelNavigator.LevelChanged</c>). Pausa o cronômetro do
    /// nível que saiu de foco e retoma (ou inicia, na primeira visita) o do que entrou.
    /// </summary>
    public void OnActiveLevel(int levelId, string name)
    {
        // 1) Trilha de navegação: registra a transição (de → para) de TODA troca de sessão — hub e
        //    níveis resolvidos incluídos. É o que revela a ordem em que o player escolhe os níveis.
        if (levelId != _previous)
        {
            _sink.LevelVisited(_previous, levelId);
            _previous = levelId;
        }

        // 2) Cronômetro/tentativa: só pra níveis-puzzle (o hub não é uma tentativa).
        if (levelId == _active)
            return;

        // Congela o tempo do nível que perdeu o foco (suspenso por um portal-filho, por exemplo).
        if (_attempts.TryGetValue(_active, out var leaving))
            leaving.Timer.Stop();

        _active = Trackable(levelId) ? levelId : None;
        if (_active == None)
            return;

        if (!_attempts.TryGetValue(levelId, out var attempt))
        {
            attempt = new Attempt { Name = name };
            _attempts[levelId] = attempt;
            _sink.LevelStarted(levelId, name);
        }
        attempt.Timer.Start(); // Start() num cronômetro já iniciado apenas RETOMA de onde pausou.
    }

    /// <summary>Um passo do player que de fato deslocou (bater na parede não conta como movimento).</summary>
    public void OnStep(int dx, int dz)
    {
        if ((dx != 0 || dz != 0) && _attempts.TryGetValue(_active, out var a))
            a.Moves++;
    }

    /// <summary>Um undo (Z) que de fato reverteu um turno.</summary>
    public void OnUndo()
    {
        if (_attempts.TryGetValue(_active, out var a))
            a.Undos++;
    }

    /// <summary>Restart leve (R): reposiciona as peças mas continua a MESMA tentativa. Só um tally.</summary>
    public void OnRestart()
    {
        if (_attempts.TryGetValue(_active, out var a))
            a.Restarts++;
    }

    /// <summary>
    /// Reset total (F): o player desistiu deste estado e recomeça do zero. Fecha a tentativa atual
    /// como abandono e abre uma nova (nova contagem de tentativa no funil).
    /// </summary>
    public void OnFullReset()
    {
        if (!_attempts.TryGetValue(_active, out var a))
            return;

        a.Timer.Stop();
        _sink.LevelAbandoned(_active, a.Name, "reset", a.Moves, a.Undos, a.Restarts, a.Timer.Elapsed.TotalSeconds);

        var fresh = new Attempt { Name = a.Name };
        _attempts[_active] = fresh;
        _sink.LevelStarted(_active, a.Name);
        fresh.Timer.Start();
    }

    /// <summary>
    /// O player pisou na meta. Chamado ANTES de o navigator concluir/descartar a sessão, então
    /// <paramref name="levelId"/> ainda é o nível vencido. Fecha a tentativa como concluída.
    /// </summary>
    public void OnWin(int levelId, string name)
    {
        if (_attempts.TryGetValue(levelId, out var a))
        {
            a.Timer.Stop();
            _sink.LevelCompleted(levelId, name, a.Moves, a.Undos, a.Restarts, a.Timer.Elapsed.TotalSeconds);
            _attempts.Remove(levelId);
        }
        // O CompleteActive que vem a seguir dispara LevelChanged → OnActiveLevel(pai), que retoma o
        // cronômetro do pai. Zerar _active aqui evita pausar uma tentativa já removida.
        if (_active == levelId)
            _active = None;
    }

    /// <summary>
    /// Fim do jogo: toda tentativa ainda aberta é um nível que o player começou e não terminou —
    /// registra cada uma como abandono ("quit") e encerra a sessão do backend.
    /// </summary>
    public void OnQuit()
    {
        foreach (var (id, a) in _attempts)
        {
            a.Timer.Stop();
            _sink.LevelAbandoned(id, a.Name, "quit", a.Moves, a.Undos, a.Restarts, a.Timer.Elapsed.TotalSeconds);
        }
        _attempts.Clear();
        _active = None;
        _sink.Shutdown();
    }
}
