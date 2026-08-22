using System.Text.Json.Nodes;

namespace PigComic.Core.Project;

/// <summary>Typed accessors over project.json "settings" with SPEC §6.2 defaults.</summary>
public sealed class ProjectSettings
{
    private readonly ProjectFile _file;

    internal ProjectSettings(ProjectFile file) => _file = file;

    private JsonObject? SettingsNode => _file.Raw["settings"] as JsonObject;

    private JsonObject QaNode => SettingsNode?["qa"] as JsonObject ?? new JsonObject();

    private static int IntVal(JsonObject node, string key, int dflt)
        => node[key] is { } n ? (n.GetValueKind() == System.Text.Json.JsonValueKind.Number ? n.GetValue<int>() : dflt) : dflt;

    private static IReadOnlyList<string> StrList(JsonObject node, string key, string[] dflt)
    {
        if (node[key] is JsonArray arr)
        {
            return arr.Where(n => n is not null).Select(n => n!.ToString()).ToList();
        }

        return dflt;
    }

    private void SetSettingsInt(string key, int value)
    {
        var s = SettingsNode;
        if (s is not null)
        {
            s[key] = value;
        }
    }

    private void SetQaInt(string key, int value)
    {
        var s = SettingsNode;
        if (s is null)
        {
            return;
        }

        var qa = s["qa"] as JsonObject ?? new JsonObject();
        qa[key] = value;
        s["qa"] = qa;
    }

    public int AutosaveSeconds
    {
        get => IntVal(SettingsNode ?? new JsonObject(), "autosaveSeconds", 180);
        set => SetSettingsInt("autosaveSeconds", value);
    }

    public int MaxCharsPerLine
    {
        get => IntVal(QaNode, "maxCharsPerLine", 8);
        set => SetQaInt("maxCharsPerLine", value);
    }

    public int MaxLinesPerPart
    {
        get => IntVal(QaNode, "maxLinesPerPart", 4);
        set => SetQaInt("maxLinesPerPart", value);
    }

    public int TcyMaxDigitRun
    {
        get => IntVal(QaNode, "tcyMaxDigitRun", 3);
        set => SetQaInt("tcyMaxDigitRun", value);
    }

    public IReadOnlyList<string> IdenticalExemptKinds
        => StrList(QaNode, "identicalExemptKinds", ["Sfx"]);

    public IReadOnlyList<string> ForbiddenTrailing
        => StrList(QaNode, "forbiddenTrailing", []);

    public IReadOnlyList<string> BracketPairs
        => StrList(QaNode, "bracketPairs", ["「」", "『』", "（）", "()", "【】"]);
}