using ClosedXML.Excel;
using PigComic.Core.Export;
using PigComic.Core.Tb;
using PigComic.Core.Tm;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>SPEC §19 XLSX / PLAN M3.9 acceptance.</summary>
public class XlsxExchangeTests : IDisposable
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
        var f = Path.Combine(Path.GetTempPath(), "pigcomic-xlsx", Guid.NewGuid().ToString("N") + ext);
        Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        _files.Add(f);
        return f;
    }

    // ---------------------------------------------------------------- TM XLSX

    [Fact]
    public async Task Tm_RoundTrip()
    {
        using var tm = new TmStore(Temp(".db"), "zh-CN", "ja");
        await tm.UpsertAsync("こんにちは", "你好", "ピッグ", "Speech", "001", null, null, CancellationToken.None);
        await tm.UpsertAsync("さようなら", "再见", null, null, "001", null, null, CancellationToken.None);

        var xlsx = Temp(".xlsx");
        var exchange = new TmXlsxExchange();
        await exchange.ExportAsync(xlsx, tm, CancellationToken.None);

        using var fresh = new TmStore(Temp("-fresh.db"), "zh-CN", "ja");
        var report = await exchange.ImportAsync(xlsx, fresh, CancellationToken.None);
        Assert.False(report.IsError, report.Error);
        Assert.Equal(2, fresh.CountEntries());
        Assert.Equal("你好", fresh.AllEntries().Single(e => e.SourceRaw == "こんにちは").TargetRaw);
        Assert.Equal("ピッグ", fresh.AllEntries().Single(e => e.SourceRaw == "こんにちは").Character);
    }

    [Fact]
    public async Task Tm_Import_Matches_Shuffled_Extra_Columns()
    {
        var xlsx = Temp("-cols.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.Worksheets.Add("TM");
            ws.Cell(1, 1).Value = "备注";     // extra, ignored
            ws.Cell(1, 2).Value = "译文";     // target
            ws.Cell(1, 3).Value = "角色";     // character
            ws.Cell(1, 4).Value = "原文";     // source
            ws.Cell(2, 2).Value = "早安";
            ws.Cell(2, 3).Value = "ピッグ";
            ws.Cell(2, 4).Value = "おはようございます";
            wb.SaveAs(xlsx);
        }

        using var fresh = new TmStore(Temp("-cols.db"), "zh-CN", "ja");
        var report = await new TmXlsxExchange().ImportAsync(xlsx, fresh, CancellationToken.None);
        Assert.False(report.IsError, report.Error);
        var e = Assert.Single(fresh.AllEntries());
        Assert.Equal("おはようございます", e.SourceRaw);
        Assert.Equal("早安", e.TargetRaw);
        Assert.Equal("ピッグ", e.Character);
    }

    [Fact]
    public async Task Tm_Import_Missing_Target_Column_Errors()
    {
        var xlsx = Temp("-bad.xlsx");
        using (var wb = new XLWorkbook())
        {
            wb.Worksheets.Add("TM").Cell(1, 1).Value = "原文";
            wb.SaveAs(xlsx);
        }

        using var fresh = new TmStore(Temp("-bad.db"), "zh-CN", "ja");
        var report = await new TmXlsxExchange().ImportAsync(xlsx, fresh, CancellationToken.None);
        Assert.True(report.IsError);
        Assert.Contains("译文", report.Error);
    }

    // ---------------------------------------------------------------- TB XLSX

    [Fact]
    public async Task Tb_RoundTrip_With_Forbidden()
    {
        using var tb = new TbStore(Temp("-tb.db"), "zh-CN", "ja");
        await tb.UpsertAsync("魔王", "大魔王", false, "注", CancellationToken.None);
        await tb.UpsertAsync("クソ", "糞", forbidden: true, "", CancellationToken.None);

        var xlsx = Temp("-tb.xlsx");
        var exchange = new TbXlsxExchange();
        await exchange.ExportAsync(xlsx, tb, CancellationToken.None);

        using var fresh = new TbStore(Temp("-tb-fresh.db"), "zh-CN", "ja");
        var report = await exchange.ImportAsync(xlsx, fresh, CancellationToken.None);
        Assert.False(report.IsError, report.Error);
        Assert.Equal(2, fresh.All().Count);
        Assert.True(fresh.All().Single(t => t.SourceTerm == "クソ").Forbidden);
        Assert.Equal("注", fresh.All().Single(t => t.SourceTerm == "魔王").Notes);
    }

    private string TempDir() => Path.GetDirectoryName(Temp(".x"))!;
}