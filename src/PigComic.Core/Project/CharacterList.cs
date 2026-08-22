using System.Text.Json.Nodes;

namespace PigComic.Core.Project;

/// <summary>
/// SPEC §6.3 characters.json master list. Rows: name (required, unique) plus
/// optional image/gender/age/firstChapter/pronoun/comments (""/absent OK).
/// Unknown properties survive round-trip (JsonNode).
/// </summary>
public sealed class CharacterList
{
    public const string FileName = "characters.json";
    private readonly string _path;
    private readonly JsonObject _root;

    public string Path => _path;

    private CharacterList(string path, JsonObject root)
    {
        _path = path;
        _root = root;
    }

    public static CharacterList Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Character file not found: {path}");
        }

        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException("characters.json must be a JSON object.");
        return new CharacterList(path, root);
    }

    public static CharacterList CreateNew(string path)
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["characters"] = new JsonArray(),
        };
        var cl = new CharacterList(path, root);
        cl.Save();
        return cl;
    }

    public sealed record MasterCharacter(
        string Name, string Image, string Gender, string Age,
        string FirstChapter, string Pronoun, string Comments);

    private static MasterCharacter FromJson(JsonNode? node) => new(
        (string?)node?["name"] ?? "",
        (string?)node?["image"] ?? "",
        (string?)node?["gender"] ?? "",
        (string?)node?["age"] ?? "",
        (string?)node?["firstChapter"] ?? "",
        (string?)node?["pronoun"] ?? "",
        (string?)node?["comments"] ?? "");

    public IReadOnlyList<MasterCharacter> Characters
    {
        get
        {
            var list = new List<MasterCharacter>();
            if (_root["characters"] is JsonArray arr)
            {
                foreach (var n in arr)
                {
                    list.Add(FromJson(n));
                }
            }

            return list;
        }
    }

    public MasterCharacter? Find(string name)
        => Characters.FirstOrDefault(c => c.Name == name);

    /// <summary>Adds or replaces a row. Returns false if a DIFFERENT row already uses the name.</summary>
    public bool AddOrUpdate(MasterCharacter character)
    {
        var arr = _root["characters"] as JsonArray ?? new JsonArray();
        var existing = arr.FirstOrDefault(n => (string?)n?["name"] == character.Name);
        if (existing is not null)
        {
            existing["image"] = character.Image;
            existing["gender"] = character.Gender ?? "";
            existing["age"] = character.Age ?? "";
            existing["firstChapter"] = character.FirstChapter ?? "";
            existing["pronoun"] = character.Pronoun ?? "";
            existing["comments"] = character.Comments ?? "";
            _root["characters"] = arr;
            Save();
            return true;
        }

        // Name uniqueness: reject strings differing only by case? Spec: unique names.
        if (arr.Any(n => string.Equals((string?)n?["name"], character.Name, StringComparison.Ordinal)))
        {
            return false;
        }

        arr.Add(new JsonObject
        {
            ["name"] = character.Name,
            ["image"] = character.Image ?? "",
            ["gender"] = character.Gender ?? "",
            ["age"] = character.Age ?? "",
            ["firstChapter"] = character.FirstChapter ?? "",
            ["pronoun"] = character.Pronoun ?? "",
            ["comments"] = character.Comments ?? "",
        });
        _root["characters"] = arr;
        Save();
        return true;
    }

    public bool Remove(string name)
    {
        if (_root["characters"] is not JsonArray arr)
        {
            return false;
        }

        var hit = arr.FirstOrDefault(c => (string?)c?["name"] == name);
        if (hit is null)
        {
            return false;
        }

        arr.Remove(hit);
        Save();
        return true;
    }

    public void Save()
    {
        File.WriteAllText(_path, _root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}