using PigComic.Core.Project;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>SPEC §6.4 / PLAN M4.2 acceptance (temp APPDATA override).</summary>
public class RegistryTests : IDisposable
{
    private readonly string _base;

    public RegistryTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "pigcomic-reg", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { }
    }

    [Fact]
    public void Add_Mru_Order_And_Touch()
    {
        var reg = new ProjectRegistry(_base);
        reg.AddOrTouch(@"C:\jobs\a\project.json");
        Thread.Sleep(5);
        reg.AddOrTouch(@"C:\jobs\b\project.json");
        reg.AddOrTouch(@"C:\jobs\c\project.json");

        Assert.Equal(
            [@"C:\jobs\c\project.json", @"C:\jobs\b\project.json", @"C:\jobs\a\project.json"],
            reg.ProjectPaths);

        Thread.Sleep(5);
        reg.AddOrTouch(@"C:\jobs\a\project.json"); // touch → MRU-first again
        Assert.Equal(
            [@"C:\jobs\a\project.json", @"C:\jobs\c\project.json", @"C:\jobs\b\project.json"],
            reg.ProjectPaths);
    }

    [Fact]
    public void Persists_Across_Instances()
    {
        var reg1 = new ProjectRegistry(_base);
        reg1.AddOrTouch(@"C:\a\project.json");
        var reg2 = new ProjectRegistry(_base);
        Assert.Equal([@"C:\a\project.json"], reg2.ProjectPaths);
    }

    [Fact]
    public void Remove_Deletes_From_Registry_Only()
    {
        var reg = new ProjectRegistry(_base);
        reg.AddOrTouch(@"C:\a\project.json");
        reg.AddOrTouch(@"C:\b\project.json");
        Assert.True(reg.Remove(@"C:\a\project.json"));
        Assert.False(reg.Remove(@"C:\a\project.json"));
        Assert.Equal([@"C:\b\project.json"], new ProjectRegistry(_base).ProjectPaths);
    }
}