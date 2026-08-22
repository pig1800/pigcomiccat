using System.Diagnostics;
using PigComic.App.Rendering;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>M2.2 acceptance: generated strips exist, correct size, JPEG &lt; 30 MB.</summary>
public class StripImageGeneratorTests
{
    [Fact]
    public void Generator_Produces_Valid_Strips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pigcomic-strips", Guid.NewGuid().ToString("N"));
        try
        {
            var sw = Stopwatch.StartNew();
            StripImageGenerator.Generate(dir);
            sw.Stop();

            var (jpeg, png) = StripImageGenerator.Verify(dir, 1000, 40000);
            Assert.True(File.Exists(jpeg));
            Assert.True(File.Exists(png));
            Assert.True(File.Exists(jpeg) && File.Exists(png));
            Assert.True(new FileInfo(jpeg).Length < 30L * 1024 * 1024);
            TestContext.WriteLine($"strip generated in {sw.ElapsedMilliseconds} ms");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}

internal static class TestContext
{
    public static void WriteLine(string s) => Console.WriteLine(s);
}