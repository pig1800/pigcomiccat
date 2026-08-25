using System.Text.Json.Nodes;
using PigComic.Core.Project;

namespace PigComic.App.Services;

/// <summary>
/// Persists the editor's three-pane splitter widths in the registry file
/// (SPEC §14.1: "widths persisted in registry.json"). The file is the same
/// %APPDATA%/PigComic/registry.json owned by <c>ProjectRegistry</c> (SPEC
/// §6.4); both sides keep every unknown JSON key on write, so the projects
/// list and the editor layout coexist. Always parse-fresh-then-merge so a
/// stale in-memory copy cannot clobber other keys.
/// </summary>
public static class EditorLayoutStore
{
    public const string LayoutKey = "editorLayout";

    public static int DefaultImageWidth => 420;
    public static int DefaultFunctionWidth => 340;

    /// <summary>Gets the persisted widths (pixels), clamped to sane bounds.</summary>
    public static (int ImageWidth, int FunctionWidth) Load()
    {
        try
        {
            var path = RegistryPath();
            if (!File.Exists(path))
            {
                return (DefaultImageWidth, DefaultFunctionWidth);
            }

            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            var layout = root?[LayoutKey] as JsonObject;
            return (
                Clamp(ReadInt(layout, "imageWidth") ?? DefaultImageWidth, 160, 2400),
                Clamp(ReadInt(layout, "functionWidth") ?? DefaultFunctionWidth, 160, 1200));
        }
        catch
        {
            return (DefaultImageWidth, DefaultFunctionWidth);
        }
    }

    /// <summary>Merges into registry.json preserving every existing key.</summary>
    public static void Save(int imageWidth, int functionWidth)
    {
        try
        {
            var path = RegistryPath();
            var root = File.Exists(path)
                ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject()
                : new JsonObject { ["schemaVersion"] = 1 };
            root[LayoutKey] = new JsonObject
            {
                ["imageWidth"] = Clamp(imageWidth, 80, 2400),
                ["functionWidth"] = Clamp(functionWidth, 160, 1200),
            };

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Layout persistence is best-effort; never fail a chapter open over it.
        }
    }

    private static string RegistryPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PigComic",
            ProjectRegistry.RegistryFileName);

    private static int? ReadInt(JsonObject? layout, string key)
        => layout?[key]?.GetValue<int>();

    private static int Clamp(int v, int lo, int hi) => Math.Clamp(v, lo, hi);
}