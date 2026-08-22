using ClosedXML.Excel;
using PigComic.Core.Adapters;
using PigComic.Core.Exchange;
using PigComic.Core.Tm;

namespace PigComic.Core.Export;

/// <summary>
/// SPEC §19 XLSX TM exchange: header row `原文 | 译文 | 角色 | 类别 | 章节`;
/// import matches columns by exact header text, order-independent (missing
/// optional columns OK, extra columns ignored); export writes all five.
/// </summary>
public sealed class TmXlsxExchange : ITmExchange
{
    public const string ColSource = "原文";
    public const string ColTarget = "译文";
    public const string ColCharacter = "角色";
    public const string ColKind = "类别";
    public const string ColChapter = "章节";

    public async Task<ImportReport> ImportAsync(string path, TmStore tm, CancellationToken ct)
        => await Task.Run<ImportReport>(async () =>
        {
            try
            {
                using var wb = new XLWorkbook(path);
                var ws = wb.Worksheets.FirstOrDefault()
                    ?? throw new InvalidOperationException("Workbook has no worksheet.");
                var headers = ReadHeaders(ws);
                if (!headers.TryGetValue(ColSource, out var srcCol) ||
                    !headers.TryGetValue(ColTarget, out var tgtCol))
                {
                    return ImportReport.Fail($"Missing required column '{ColSource}' or '{ColTarget}'.");
                }

                headers.TryGetValue(ColKind, out var kindCol);
                headers.TryGetValue(ColChapter, out var chapterCol);
                int? charCol2 = headers.TryGetValue(ColCharacter, out var cc) ? cc : null;
                int? kindCol2 = headers.TryGetValue(ColKind, out var kc) ? kc : null;
                int? chapterCol2 = headers.TryGetValue(ColChapter, out var hc) ? hc : null;

                var added = 0;
                var updated = 0;
                var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
                for (var row = 2; row <= lastRow; row++)
                {
                    ct.ThrowIfCancellationRequested();
                    var src = ws.Cell(row, srcCol).GetString();
                    var tgt = ws.Cell(row, tgtCol).GetString();
                    if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(tgt))
                    {
                        continue;
                    }

                    var character = charCol2 is null ? null : Str(ws.Cell(row, charCol2.Value));
                    var kind = kindCol2 is null ? null : Str(ws.Cell(row, kindCol2.Value));
                    var chapter = chapterCol2 is null ? null : Str(ws.Cell(row, chapterCol2.Value));

                    var norm = Normalizer.Normalize(src, tm.SourceLanguage);
                    var exists = tm.AllEntries().Any(e =>
                        e.SourceHash == TmHash.Compute(norm) &&
                        (e.Character ?? "") == (character ?? ""));
                    var result = await tm.UpsertAsync(src, tgt, character, kind, chapter, null, null, ct);
                    if (result is null)
                    {
                        continue;
                    }

                    if (exists)
                    {
                        updated++;
                    }
                    else
                    {
                        added++;
                    }
                }

                return new ImportReport(added, updated, 0);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ImportReport.Fail(ex.Message);
            }
        }, ct);

    public Task ExportAsync(string path, TmStore tm, CancellationToken ct)
        => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("TM");
            ws.Cell(1, 1).Value = ColSource;
            ws.Cell(1, 2).Value = ColTarget;
            ws.Cell(1, 3).Value = ColCharacter;
            ws.Cell(1, 4).Value = ColKind;
            ws.Cell(1, 5).Value = ColChapter;

            var row = 2;
            foreach (var e in tm.AllEntries())
            {
                ws.Cell(row, 1).Value = e.SourceRaw;
                ws.Cell(row, 2).Value = e.TargetRaw;
                ws.Cell(row, 3).Value = e.Character ?? "";
                ws.Cell(row, 4).Value = e.Kind ?? "";
                ws.Cell(row, 5).Value = e.Chapter ?? "";
                row++;
            }

            wb.SaveAs(path);
        }, ct);

    private static Dictionary<string, int> ReadHeaders(IXLWorksheet ws)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var lastCol = ws.LastCellUsed()?.Address.ColumnNumber ?? 1;
        for (var c = 1; c <= lastCol; c++)
        {
            var header = ws.Cell(1, c).GetString().Trim();
            if (header.Length > 0 && !map.ContainsKey(header))
            {
                map[header] = c;
            }
        }

        return map;
    }

    private static string? Str(IXLCell cell)
    {
        var v = cell.GetString().Trim();
        return v.Length == 0 ? null : v;
    }
}