using System.Text.Json;
using System.Text.Json.Nodes;

namespace PigComic.Core.Project;

/// <summary>
/// SPEC §6.2 project.json — read/written via JsonNode round-trip (D-07) so
/// unknown properties survive save. Language pair is fixed at creation; a
/// package with a different pair refuses to open into this project (D-28).
/// </summary>
public sealed class ProjectFile
{
    public const string FileName = "project.json";
    public const int SchemaVersion = 1;

    private readonly JsonNode _root;
    private readonly string _path;
    private bool _dirty;

    public string Path { get; }

    private ProjectFile(string path, JsonNode root)
    {
        _path = path;
        _root = root;
        Path = path;
    }

    public static ProjectFile Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Project file not found: {path}");
        }

        var json = File.ReadAllText(path);
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException("project.json must be a JSON object.");
        return new ProjectFile(path, root);
    }

    public static ProjectFile CreateNew(string path, string title, string sourceLanguage, string targetLanguage)
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["title"] = title,
            ["sourceLanguage"] = sourceLanguage,
            ["targetLanguage"] = targetLanguage,
            ["chapters"] = new JsonArray(),
            ["settings"] = new JsonObject
            {
                ["autosaveSeconds"] = 180,
                ["qa"] = new JsonObject
                {
                    ["maxCharsPerLine"] = 8,
                    ["maxLinesPerPart"] = 4,
                    ["tcyMaxDigitRun"] = 3,
                    ["identicalExemptKinds"] = new JsonArray("Sfx"),
                    ["forbiddenTrailing"] = new JsonArray(),
                    ["bracketPairs"] = new JsonArray("「」", "『』", "（）", "()", "【】"),
                },
                ["export"] = new JsonObject
                {
                    ["includeDataSheet"] = false,
                    ["defaultFont"] = "",
                },
                ["llmQa"] = new JsonObject
                {
                    ["provider"] = "claude",
                    ["model"] = "claude-opus-5",
                    ["temperature"] = 0.2,
                },
            },
        };
        var pf = new ProjectFile(path, root);
        pf.Save();
        return pf;
    }

    private JsonObject Root => (JsonObject)_root;

    public string Title
    {
        get => (string?)Root["title"] ?? "";
        set { Root["title"] = value; _dirty = true; }
    }

    public string SourceLanguage
    {
        get => (string?)Root["sourceLanguage"] ?? "";
        set { Root["sourceLanguage"] = value; _dirty = true; }
    }

    public string TargetLanguage
    {
        get => (string?)Root["targetLanguage"] ?? "";
        set { Root["targetLanguage"] = value; _dirty = true; }
    }

    public IReadOnlyList<string> ChapterPaths
    {
        get
        {
            var list = new List<string>();
            if (Root["chapters"] is JsonArray arr)
            {
                foreach (var node in arr)
                {
                    if (node?["path"] is { } p)
                    {
                        list.Add(p.ToString());
                    }
                }
            }

            return list;
        }
    }

    /// <summary>Adds a chapter pair; returns false if the path is already present.</summary>
    public bool AddChapter(string path)
    {
        var arr = Root["chapters"] as JsonArray ?? new JsonArray();
        if (arr.Any(n => n?["path"]?.ToString() == path))
        {
            return false;
        }

        arr.Add(new JsonObject { ["path"] = path });
        Root["chapters"] = arr;
        _dirty = true;
        return true;
    }

    public bool RemoveChapter(string path)
    {
        if (Root["chapters"] is not JsonArray arr)
        {
            return false;
        }

        var toRemove = arr.FirstOrDefault(n => n?["path"]?.ToString() == path);
        if (toRemove is null)
        {
            return false;
        }

        arr.Remove(toRemove);
        _dirty = true;
        return true;
    }

    /// <summary>Reorders chapters by the given full list of paths (array order = display order).</summary>
    public void ReorderChapters(IReadOnlyList<string> pathsInOrder)
    {
        if (Root["chapters"] is not JsonArray arr)
        {
            return;
        }

        var existing = arr.ToList();
        arr.Clear();
        foreach (var path in pathsInOrder)
        {
            var match = existing.FirstOrDefault(n => n?["path"]?.ToString() == path);
            if (match is not null)
            {
                arr.Add(match);
            }
        }

        _dirty = true;
    }

    public ProjectSettings Settings => new(this);

    public bool IsDirty => _dirty;

    /// <summary>Saves via the JsonNode round-trip path (unknown properties survive).</summary>
    public void Save()
    {
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_path, _root.ToJsonString(options));
        _dirty = false;
    }

    internal JsonNode Raw => _root;
}