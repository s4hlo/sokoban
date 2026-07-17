namespace Sokoban3D.Analytics;

/// <summary>
/// Backend nulo (padrão Null Object): descarta todos os eventos. É o que roda quando não há chaves
/// do GameAnalytics configuradas (build de dev sem telemetria, testes, contribuidor sem conta).
/// Deixa o resto do código chamar <see cref="IAnalytics"/> sem checar nulo em lugar nenhum.
/// </summary>
public sealed class NullAnalytics : IAnalytics
{
    public void LevelStarted(int levelId, string levelName) { }
    public void LevelCompleted(int levelId, string levelName, int moves, int undos, int restarts, double seconds) { }
    public void LevelAbandoned(int levelId, string levelName, string reason, int moves, int undos, int restarts, double seconds) { }
    public void LevelVisited(int fromLevelId, int toLevelId) { }
    public void Shutdown() { }
}
