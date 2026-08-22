using PigComic.Core.Exchange;
using PigComic.Core.Tm;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>SPEC §19 TMX / PLAN M3.7 acceptance.</summary>
public class TmxTests : IDisposable
{
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var f in _files)
        {
            try { File.Delete(f); } catch { }
            try { File.Delete(f + "-wal"); } catch { }
            try { File.Delete(f + "-shm"); } catch { }
        }
    }

    private string Temp(string ext)
    {
        var f = Path.Combine(Path.GetTempPath(), "pigcomic-tmx", Guid.NewGuid().ToString("N") + ext);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        _files.Add(f);
        return f;
    }

    [Fact]
    public async Task Export_Then_Import_RoundTrips_Identically()
    {
        using var tm = new TmStore(Temp(".db"), "ja", "zh-Hant");
        await tm.UpsertAsync("おはようございます", "早安", "ピッグ", "Speech", "012", null, null, CancellationToken.None);
        await tm.UpsertAsync("魔王が来た", "魔王来了", null, null, "012", null, null, CancellationToken.None);
        await tm.UpsertAsync("ドドド", "咚", null, null, "012", null, null, CancellationToken.None);

        var tmxPath = Temp(".tmx");
        var exchange = new TmxExchange();
        await exchange.ExportAsync(tmxPath, tm, CancellationToken.None);

        using var fresh = new TmStore(Temp("-fresh.db"), "ja", "zh-Hant");
        var report = await exchange.ImportAsync(tmxPath, fresh, CancellationToken.None);

        Assert.False(report.IsError, report.Error);
        Assert.Equal(3, fresh.CountEntries());

        var orig = tm.AllEntries().OrderBy(e => e.SourceRaw).ToList();
        var re = fresh.AllEntries().OrderBy(e => e.SourceRaw).ToList();
        Assert.Equal(orig.Count, re.Count);
        for (var i = 0; i < orig.Count; i++)
        {
            Assert.Equal(orig[i].SourceRaw, re[i].SourceRaw);
            Assert.Equal(orig[i].TargetRaw, re[i].TargetRaw);
            Assert.Equal(orig[i].Character ?? "", re[i].Character ?? "");
            Assert.Equal(orig[i].Kind ?? "", re[i].Kind ?? "");
            Assert.Equal(orig[i].Chapter ?? "", re[i].Chapter ?? "");
        }
    }

    [Fact]
    public async Task Import_Strips_Inline_Tags_And_Reports_One_Warning()
    {
        var path = Temp(".tmx");
        var tmx =
            """
            <tmx version="1.4" xmlns="http://www.lisa.org/tmx14">
            <header srclang="ja" datatype="unknown"/>
            <body>
            <tu>
              <tuv xml:lang="ja"><seg>こん<bpt>&lt;b&gt;</bpt>にちは<ept>&lt;/b&gt;</ept></seg></tuv>
              <tuv xml:lang="zh-Hant"><seg>你<it pos="begin">好</it></seg></tuv>
            </tu>
            </body>
            </tmx>
            """;
        File.WriteAllText(path, tmx);

        using var fresh = new TmStore(Temp("-tag.db"), "ja", "zh-Hant");
        var report = await new TmxExchange().ImportAsync(path, fresh, CancellationToken.None);

        Assert.False(report.IsError, report.Error);
        Assert.True(report.TagStripped >= 1, $"expected ≥1 stripped, got {report.TagStripped}");
        // seg Value never contains markup — tags effectively stripped.
        var entry = Assert.Single(fresh.AllEntries());
        Assert.Contains("こん", entry.SourceRaw);
        Assert.DoesNotContain("<bpt>", entry.SourceRaw);
        Assert.DoesNotContain("<", entry.SourceRaw);
    }

    [Fact]
    public async Task WrongLanguage_TMX_Errors_Listing_Languages()
    {
        var path = Temp(".tmx");
        File.WriteAllText(path,
            """
            <tmx version="1.4" xmlns="http://www.lisa.org/tmx14">
            <header srclang="ko" datatype="unknown"/>
            <body><tu><tuv xml:lang="ko"><seg>안녕</seg></tuv>
            <tuv xml:lang="en"><seg>hello</seg></tuv></tu></body>
            </tmx>
            """);

        using var fresh = new TmStore(Temp("-wrong.db"), "ja", "zh-Hant");
        var report = await new TmxExchange().ImportAsync(path, fresh, CancellationToken.None);
        Assert.True(report.IsError);
        Assert.Contains("ko", report.Error);
        Assert.Contains("en", report.Error);
    }
}