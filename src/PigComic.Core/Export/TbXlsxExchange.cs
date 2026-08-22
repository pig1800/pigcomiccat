using ClosedXML.Excel;
using PigComic.Core.Adapters;
using PigComic.Core.Tb;

namespace PigComic.Core.Export;

/// <summary>
/// SPEC §19 XLSX TB exchange: header row `原文 | 译文 | 禁止 | 备注`; import
/// matches columns by exact header text (order-independent, extra columns
/// ignored); any non-empty `禁止` cell marks the row forbidden=1.
/// </summary>
public sealed class TbXlsxExchange : ITbExchange
{
    public const string ColSource = "原文";
    public const string ColTarget = "译文";
    public const string ColForbidden = "禁止";
    public const string ColNotes = "备注";

    public async Task<ImportReport> ImportAsync(string path, TbStore tb, CancellationToken ct)
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

                headers.TryGetValue(ColNotes, out var noteCol);
                int? forbCol2 = headers.TryGetValue(ColForbidden, out var fc) ? fc : null;
                int? noteCol2 = headers.TryGetValue(ColNotes, out var nc) ? nc : null;

                var added = 0;
                var updated = 0;
                var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
                for (var row = 2; row <= lastRow; row++)
                {
                    ct.ThrowIfCancellationRequested();
                    var src = ws.Cell(row, srcCol).GetString();
                    if (string.IsNullOrWhiteSpace(src))
                    {
                        continue;
                    }

                    var tgt = ws.Cell(row, tgtCol).GetString();
                    var forbidden = forbCol2 is not null && ws.Cell(row, forbCol2.Value).GetString().Trim().Length > 0;
                    if (!forbidden && string.IsNullOrWhiteSpace(tgt))
                    {
                        continue;
                    }

                    var notes = noteCol2 is null ? "" : ws.Cell(row, noteCol2.Value).GetString();
                    var exists = tb.All().Any(t => t.SourceTerm == src);
                    await tb.UpsertAsync(src, tgt, forbidden, notes, ct);
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

    public Task ExportAsync(string path, TbStore tb, CancellationToken ct)
        => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("TB");
            ws.Cell(1, 1).Value = ColSource;
            ws.Cell(1, 2).Value = ColTarget;
            ws.Cell(1, 3).Value = ColForbidden;
            ws.Cell(1, 4).Value = ColNotes;

            var row = 2;
            foreach (var t in tb.All())
            {
                ws.Cell(row, 1).Value = t.SourceTerm;
                ws.Cell(row, 2).Value = t.TargetTerm;
                ws.Cell(row, 3).Value = t.Forbidden ? "禁止" : "";
                ws.Cell(row, 4).Value = t.Notes;
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
}