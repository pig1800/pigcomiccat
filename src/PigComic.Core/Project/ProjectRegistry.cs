using System.Text.Json.Nodes;

namespace PigComic.Core.Project;

/// <summary>
/// SPEC §6.4 projects registry: %APPDATA%/PigComic/registry.json, projects
/// sorted most-recently-opened first. Base-dir overridable for tests.
/// </summary>
public sealed class ProjectRegistry
{
    private sealed record Entry(string ProjectJsonPath, string LastOpenedUtc);

    private readonly string _registryPath;
    private readonly JsonObject _root;
    private readonly List<Entry> _entries = [];

    public const string RegistryFileName = "registry.json";

    public static string DefaultRegistryPath
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PigComic",
            RegistryFileName);

    public ProjectRegistry(string? baseDir = null)
    {
        _registryPath = baseDir is null
            ? DefaultRegistryPath
            : Path.Combine(baseDir, "PigComic", RegistryFileName);
        if (File.Exists(_registryPath))
        {
            _root = JsonNode.Parse(File.ReadAllText(_registryPath)) as JsonObject ?? new JsonObject();
        }
        else
        {
            _root = new JsonObject { ["schemaVersion"] = 1, ["projects"] = new JsonArray() };
        }

        if (_root["projects"] is JsonArray arr)
        {
            foreach (var n in arr)
            {
                var path = (string?)n?["projectJsonPath"] ?? "";
                var utc = (string?)n?["lastOpenedUtc"] ?? "";
                if (path.Length > 0)
                {
                    _entries.Add(new Entry(path, utc));
                }
            }
        }

        _entries.Sort((a, b) => string.CompareOrdinal(b.LastOpenedUtc, a.LastOpenedUtc));
    }

    /// <summary>Registered projects, MRU-first.</summary>
    public IReadOnlyList<string> ProjectPaths => _entries.Select(e => e.ProjectJsonPath).ToList();

    /// <summary>Registers a project file (creates the registry when absent).</summary>
    public void AddOrTouch(string projectJsonPath)
    {
        var full = Path.GetFullPath(projectJsonPath);
        var utc = DateTime.UtcNow.ToString("O");
        _entries.RemoveAll(e => string.Equals(e.ProjectJsonPath, full, StringComparison.OrdinalIgnoreCase));
        _entries.Insert(0, new Entry(full, utc));
        Save();
    }

    /// <summary>Removes from the list only (no file deletion — that is RemoveProjectDialog's job).</summary>
    public bool Remove(string projectJsonPath)
    {
        var full = Path.GetFullPath(projectJsonPath);
        var removed = _entries.RemoveAll(e => string.Equals(e.ProjectJsonPath, full, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            Save();
        }

        return removed;
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(_registryPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var arr = new JsonArray();
        foreach (var e in _entries)
        {
            arr.Add(new JsonObject
            {
                ["projectJsonPath"] = e.ProjectJsonPath,
                ["lastOpenedUtc"] = e.LastOpenedUtc,
            });
        }

        _root["projects"] = arr;
        File.WriteAllText(_registryPath, _root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}