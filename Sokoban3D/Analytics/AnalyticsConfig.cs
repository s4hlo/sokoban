using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Sokoban3D.Analytics;

/// <summary>
/// Credenciais do GameAnalytics. Ficam FORA do código-fonte (a secret key não pode ir pro git):
/// lê primeiro das variáveis de ambiente <c>GAMEANALYTICS_GAME_KEY</c> / <c>GAMEANALYTICS_SECRET_KEY</c>,
/// e, se faltarem, de um <c>analytics.json</c> ao lado do executável (ignorado pelo git). Sem chaves,
/// <see cref="TryLoad"/> devolve false e o jogo roda com telemetria desligada (<see cref="NullAnalytics"/>).
/// </summary>
public readonly struct AnalyticsConfig
{
    public readonly string GameKey;
    public readonly string SecretKey;
    public readonly string Build;

    private AnalyticsConfig(string gameKey, string secretKey, string build)
    {
        GameKey = gameKey;
        SecretKey = secretKey;
        Build = build;
    }

    public static bool TryLoad(out AnalyticsConfig config)
    {
        var (gameKey, secretKey) = FromEnvironment();
        if (string.IsNullOrWhiteSpace(gameKey) || string.IsNullOrWhiteSpace(secretKey))
            (gameKey, secretKey) = FromFile();

        if (string.IsNullOrWhiteSpace(gameKey) || string.IsNullOrWhiteSpace(secretKey))
        {
            config = default;
            return false;
        }

        config = new AnalyticsConfig(gameKey, secretKey, BuildVersion());
        return true;
    }

    private static (string, string) FromEnvironment()
        => (Environment.GetEnvironmentVariable("GAMEANALYTICS_GAME_KEY"),
            Environment.GetEnvironmentVariable("GAMEANALYTICS_SECRET_KEY"));

    private static (string, string) FromFile()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "analytics.json");
            if (!File.Exists(path))
                return (null, null);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            string game = root.TryGetProperty("gameKey", out var g) ? g.GetString() : null;
            string secret = root.TryGetProperty("secretKey", out var s) ? s.GetString() : null;
            return (game, secret);
        }
        catch (Exception)
        {
            // Arquivo ilegível não deve derrubar nada: apenas desliga a telemetria.
            return (null, null);
        }
    }

    /// <summary>Versão do build no formato <c>major.minor.patch</c>, pra separar métricas por versão.</summary>
    private static string BuildVersion()
    {
        var v = Assembly.GetEntryAssembly()?.GetName().Version;
        return v is null ? "0.1.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
