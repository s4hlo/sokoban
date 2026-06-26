using System.Collections.Generic;
using System.IO;

namespace Sokoban3D.Levels;

/// <summary>
/// Guarda os níveis como arquivos JSON num diretório (<c>Levels/</c>). É a fonte de verdade
/// dos mapas: o <see cref="LevelCatalog"/> lê daqui e o editor grava aqui — então editar e
/// salvar (F5) altera o mapa oficial de vez. O diretório é resolvido relativo ao diretório de
/// trabalho atual (onde o jogo roda).
/// </summary>
public class LevelRepository
{
    private const string Prefix = "level_";

    /// <summary>Diretório onde os mapas ficam.</summary>
    public string Dir { get; }

    public LevelRepository(string dir = null)
    {
        // "Maps" (e não "Levels") pra não colidir com a pasta de código-fonte Levels/.
        Dir = dir ?? Path.Combine(Directory.GetCurrentDirectory(), "Maps");
    }

    public string PathFor(int id) => Path.Combine(Dir, $"{Prefix}{id}.json");

    public bool Exists(int id) => File.Exists(PathFor(id));

    public Level Load(int id) => LevelSerializer.Load(PathFor(id));

    public void Save(Level level) => LevelSerializer.Save(level, PathFor(level.Id));

    /// <summary>Ids de todos os mapas presentes no diretório (pela convenção level_&lt;id&gt;.json).</summary>
    public IEnumerable<int> ListIds()
    {
        if (!Directory.Exists(Dir))
            yield break;

        foreach (var file in Directory.EnumerateFiles(Dir, $"{Prefix}*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (int.TryParse(name.Substring(Prefix.Length), out int id))
                yield return id;
        }
    }
}
