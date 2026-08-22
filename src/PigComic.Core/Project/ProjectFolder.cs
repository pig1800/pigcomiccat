using PigComic.Core.Data;
using PigComic.Core.Tb;
using PigComic.Core.Tm;

namespace PigComic.Core.Project;

/// <summary>
/// SPEC §6.1 project folder: create-new writes project.json, characters.json and
/// empty tm.db / tb.db. .pcml files are referenced by absolute path, never
/// copied into the folder.
/// </summary>
public static class ProjectFolder
{
    public static void Create(string folderPath, string title, string sourceLanguage, string targetLanguage)
    {
        if (Directory.Exists(folderPath) && Directory.EnumerateFileSystemEntries(folderPath).Any())
        {
            throw new InvalidOperationException($"Folder must be empty or nonexistent: {folderPath}");
        }

        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(Path.Combine(folderPath, "characters"));

        var projectJson = Path.Combine(folderPath, ProjectFile.FileName);
        if (!File.Exists(projectJson))
        {
            ProjectFile.CreateNew(projectJson, title, sourceLanguage, targetLanguage);
        }

        var charactersJson = Path.Combine(folderPath, CharacterList.FileName);
        if (!File.Exists(charactersJson))
        {
            CharacterList.CreateNew(charactersJson);
        }

        // Empty TM/TB — creation registers the language pair.
        using var tm = new TmStore(Path.Combine(folderPath, "tm.db"), sourceLanguage, targetLanguage);
        using var tb = new TbStore(Path.Combine(folderPath, "tb.db"), sourceLanguage, targetLanguage);
    }

    /// <summary>Opens the project's stores (throws when languages conflict).</summary>
    public static (TmStore Tm, TbStore Tb) OpenStores(string folderPath, string sourceLanguage, string targetLanguage)
    {
        var tm = new TmStore(Path.Combine(folderPath, "tm.db"), sourceLanguage, targetLanguage);
        try
        {
            var tb = new TbStore(Path.Combine(folderPath, "tb.db"), sourceLanguage, targetLanguage);
            return (tm, tb);
        }
        catch
        {
            tm.Dispose();
            throw;
        }
    }
}