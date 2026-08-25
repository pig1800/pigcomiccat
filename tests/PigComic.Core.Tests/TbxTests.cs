using PigComic.Core.Exchange;
using PigComic.Core.Tb;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>SPEC §19 TBX / PLAN M3.8 acceptance.</summary>
public class TbxTests : IDisposable
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
        var f = Path.Combine(Path.GetTempPath(), "pigcomic-tbx", Guid.NewGuid().ToString("N") + ext);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        _files.Add(f);
        return f;
    }

    [Fact]
    public async Task RoundTrip_Three_Terms_Including_Forbidden()
    {
        using var tb = new TbStore(Temp(".db"), "zh-CN", "ja");
        await tb.UpsertAsync("魔王", "大魔王", false, "注1", CancellationToken.None);
        await tb.UpsertAsync("ゲス", "混蛋", false, "", CancellationToken.None);
        await tb.UpsertAsync("クソ", "糞", forbidden: true, "", CancellationToken.None);

        var tbxPath = Temp(".tbx");
        var exchange = new TbxExchange();
        await exchange.ExportAsync(tbxPath, tb, CancellationToken.None);

        using var fresh = new TbStore(Temp("-fresh.db"), "zh-CN", "ja");
        var report = await exchange.ImportAsync(tbxPath, fresh, CancellationToken.None);
        Assert.False(report.IsError, report.Error);
        Assert.Equal(3, fresh.All().Count);

        var forbidden = fresh.All().Single(t => t.SourceTerm == "クソ");
        Assert.True(forbidden.Forbidden, "forbidden marker must round-trip to forbidden=1");
        Assert.Equal("大魔王", fresh.All().Single(t => t.SourceTerm == "魔王").TargetTerm);
    }

    [Fact]
    public async Task Import_TBXMin_Example()
    {
        var path = Temp(".tbx");
        File.WriteAllText(path,
            """
            <?xml version="1.0"?>
            <martif type="TBX-Min">
              <martifHeader><fileDesc><sourceDesc><p>test</p></sourceDesc></fileDesc></martifHeader>
              <text><body>
                <termEntry>
                  <langSet xml:lang="ja"><tig><term>勇者</term></tig></langSet>
                  <langSet xml:lang="zh-Hant"><tig><term>勇者</term></tig></langSet>
                </termEntry>
              </body></text>
            </martif>
            """);

        using var fresh = new TbStore(Temp("-min.db"), "zh-CN", "ja");
        var report = await new TbxExchange().ImportAsync(path, fresh, CancellationToken.None);
        Assert.False(report.IsError, report.Error);
        Assert.Equal(1, fresh.All().Count);
        Assert.Equal("勇者", fresh.All()[0].SourceTerm);
    }
}