using System;
using System.Globalization;
using GameAnalyticsSDK.Net;
using Serilog;
using Sokoban3D.Levels;

namespace Sokoban3D.Analytics;

/// <summary>
/// Backend de telemetria em cima do SDK do GameAnalytics. É o ÚNICO arquivo que fala a API do GA —
/// todo o resto do jogo depende só de <see cref="IAnalytics"/>, então trocar de provedor ou mexer
/// na tradução dos eventos fica contido aqui.
///
/// Tradução dos eventos de domínio pro vocabulário do GA:
/// <list type="bullet">
/// <item><b>Progression events</b> (funil de retenção): Start ao entrar, Complete/Fail ao sair.
///   O GA deriva daí a taxa de conclusão, o drop por nível e o nº de tentativas — o cerne de
///   "onde os players travam".</item>
/// <item><b>Design events</b>: métricas contínuas que o progression não carrega — tempo, movimentos,
///   undos e restarts por nível. É o que alimenta a decisão de reordenar (dificuldade percebida).</item>
/// </list>
/// Toda chamada ao SDK é blindada: telemetria nunca pode lançar no loop do jogo.
/// </summary>
public sealed class GameAnalyticsSink : IAnalytics
{
    private GameAnalyticsSink() { }

    /// <summary>
    /// Inicializa o GA se houver chaves configuradas. Devolve o sink pronto, ou <c>null</c> se não há
    /// credenciais / a inicialização falhou — nesse caso o chamador cai no <see cref="NullAnalytics"/>.
    /// </summary>
    public static IAnalytics TryCreate()
    {
        if (!AnalyticsConfig.TryLoad(out var config))
        {
            Log.Information("GameAnalytics: sem chaves configuradas — telemetria desligada.");
            return null;
        }

        try
        {
            GameAnalytics.ConfigureBuild(config.Build);
            GameAnalytics.Initialize(config.GameKey, config.SecretKey);
            Log.Information("GameAnalytics inicializado (build {Build}).", config.Build);
            return new GameAnalyticsSink();
        }
        catch (Exception e)
        {
            Log.Warning(e, "GameAnalytics: falha na inicialização — telemetria desligada.");
            return null;
        }
    }

    public void LevelStarted(int levelId, string levelName)
        => Guard(() => GameAnalytics.AddProgressionEvent(EGAProgressionStatus.Start, Slug(levelId)));

    public void LevelCompleted(int levelId, string levelName, int moves, int undos, int restarts, double seconds)
        => Guard(() =>
        {
            string slug = Slug(levelId);
            GameAnalytics.AddProgressionEvent(EGAProgressionStatus.Complete, slug, (double)moves);
            GameAnalytics.AddDesignEvent($"{slug}:complete:time", (float)seconds);
            GameAnalytics.AddDesignEvent($"{slug}:complete:moves", moves);
            GameAnalytics.AddDesignEvent($"{slug}:complete:undos", undos);
            GameAnalytics.AddDesignEvent($"{slug}:complete:restarts", restarts);
        });

    public void LevelAbandoned(int levelId, string levelName, string reason, int moves, int undos, int restarts, double seconds)
        => Guard(() =>
        {
            string slug = Slug(levelId);
            GameAnalytics.AddProgressionEvent(EGAProgressionStatus.Fail, slug, (double)moves);
            GameAnalytics.AddDesignEvent($"{slug}:abandon:{Sanitize(reason)}", (float)seconds);
        });

    public void LevelVisited(int fromLevelId, int toLevelId)
        => Guard(() => GameAnalytics.AddDesignEvent($"nav:{NavSlug(fromLevelId)}:{NavSlug(toLevelId)}"));

    public void Shutdown() => Guard(GameAnalytics.OnQuit);

    // Tier de progression estável por id de nível. ATENÇÃO: o id é o SLOT de ordenação — reordenar
    // níveis (Shift+W/S no menu) reaproveita ids, então métricas antigas de um slot se misturam com
    // o novo puzzle daquele slot. Meça com a ordem congelada; ao reordenar, suba o build pra separar.
    private static string Slug(int levelId) => "level_" + levelId.ToString("00", CultureInfo.InvariantCulture);

    // Rótulo da trilha de navegação: o hub e o início da sessão viram nomes legíveis pro fluxo
    // (nav:start:hub, nav:hub:level_05, nav:level_05:hub) — o resto usa o slug normal do nível.
    private static string NavSlug(int levelId)
        => levelId < 0 ? "start" : levelId == LevelCatalog.RootId ? "hub" : Slug(levelId);

    // Segmento de eventId do GA aceita só [A-Za-z0-9 _-.()!?]. Normaliza texto livre (o "reason").
    private static string Sanitize(string s) => string.IsNullOrEmpty(s) ? "unknown" : s.ToLowerInvariant();

    private static void Guard(Action ga)
    {
        try { ga(); }
        catch (Exception e) { Log.Warning(e, "GameAnalytics: evento descartado por erro."); }
    }
}
