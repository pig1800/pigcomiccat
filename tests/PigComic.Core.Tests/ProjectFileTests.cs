using System.Text.Json;
using System.Text.Json.Nodes;
using PigComic.Core.Project;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>SPEC §6.1-§6.3 / PLAN M4.1 acceptance.</summary>
public class ProjectFileTests : IDisposable
{
    private readonly string _dir;

    public ProjectFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pigcomic-proj", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Create_Then_Load_Has_Defaults()
    {
        var path = Path.Combine(_dir, "project.json");
        ProjectFile.CreateNew(path, "勇者小猪", "zh-CN", "ja");

        var pf = ProjectFile.Load(path);
        Assert.Equal("勇者小猪", pf.Title);
        Assert.Equal("zh-CN", pf.SourceLanguage);
        Assert.Equal("ja", pf.TargetLanguage);
        Assert.Empty(pf.ChapterPaths);
        Assert.Equal(180, pf.Settings.AutosaveSeconds);
        Assert.Equal(8, pf.Settings.MaxCharsPerLine);
        Assert.Equal(["Sfx"], pf.Settings.IdenticalExemptKinds);
        Assert.Equal(5, pf.Settings.BracketPairs.Count);
    }

    [Fact]
    public void Unknown_Property_Survives_Load_Save()
    {
        File.WriteAllText(Path.Combine(_dir, "project.json"),
            """
            { "schemaVersion": 1, "title": "t", "sourceLanguage": "ja",
              "targetLanguage": "zh-Hant", "chapters": [],
              "vendorData": { "custom": 42 } }
            """);
        var pf = ProjectFile.Load(Path.Combine(_dir, "project.json"));
        pf.AddChapter("C:\\jobs\\ch1.pcml");
        pf.Save();

        var reloaded = JsonNode.Parse(File.ReadAllText(Path.Combine(_dir, "project.json")))!;
        Assert.Equal(42, reloaded["vendorData"]?["custom"]?.GetValue<int>());
    }

    [Fact]
    public void Chapter_Add_Remove_RoundTrip()
    {
        var path = Path.Combine(_dir, "project.json");
        ProjectFile.CreateNew(path, "t", "zh-CN", "ja");
        var pf = ProjectFile.Load(path);
        Assert.True(pf.AddChapter("C:\\a\\ch1.pcml"));
        Assert.False(pf.AddChapter("C:\\a\\ch1.pcml")); // duplicate rejected
        pf.AddChapter("C:\\a\\ch2.pcml");
        Assert.Equal(2, pf.ChapterPaths.Count);
        Assert.True(pf.RemoveChapter("C:\\a\\ch1.pcml"));
        Assert.Single(pf.ChapterPaths);
        pf.Save();

        var reloaded = ProjectFile.Load(path);
        Assert.Equal(["C:\\a\\ch2.pcml"], reloaded.ChapterPaths);
    }

    [Fact]
    public void CharacterStore_Add_Remove_RoundTrip()
    {
        var path = Path.Combine(_dir, "characters.db");
        using (var store = new CharacterStore(path))
        {
            var pig = new CharacterStore.MasterCharacter("ピッグ", "ブタ", "buta", null, "male", "16", "001", "オレ", "語尾「〜だぜ」");
            store.AddOrUpdate(pig);
            store.AddOrUpdate(new CharacterStore.MasterCharacter("魔王", "", "", null, "", "", "", "", ""));

            Assert.Equal(2, store.LoadAll().Count);
            // Name uniqueness: adding the same name again updates the row, one row only.
            store.AddOrUpdate(pig with { Localized = "マオウ" });
            var all = store.LoadAll();
            Assert.Equal(2, all.Count);
            Assert.Equal("マオウ", store.Find("ピッグ")!.Localized);

            Assert.True(store.Remove("ピッグ"));
            Assert.Single(store.LoadAll());
        }
    }

    [Fact]
    public void CreateNew_Project_Folder_Produces_Layout_With_Openable_Stores()
    {
        var folder = Path.Combine(_dir, "MyManga");
        ProjectFolder.Create(folder, "勇者小猪", "zh-CN", "ja");

        Assert.True(File.Exists(Path.Combine(folder, "project.json")));
        Assert.True(File.Exists(Path.Combine(folder, "characters.db")));
        Assert.True(File.Exists(Path.Combine(folder, "tm.db")));
        Assert.True(File.Exists(Path.Combine(folder, "tb.db")));

        var (tm, tb) = ProjectFolder.OpenStores(folder, "zh-CN", "ja");
        using (tm)
        using (tb)
        {
            Assert.Equal(0L, tm.CountEntries());
        }
    }

    [Fact]
    public void Create_Refuses_NonEmpty_Folder()
    {
        var folder = Path.Combine(_dir, "Occupied");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "x.txt"), "x");
        Assert.Throws<InvalidOperationException>(() => ProjectFolder.Create(folder, "t", "zh-CN", "ja"));
    }
}