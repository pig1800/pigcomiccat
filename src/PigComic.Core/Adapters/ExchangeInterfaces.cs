using PigComic.Core.Tb;
using PigComic.Core.Tm;

namespace PigComic.Core.Adapters;

/// <summary>Import outcome: added / updated / skipped / tag-stripped counts (SPEC §19).</summary>
public sealed record ImportReport(
    int Added,
    int Updated,
    int Skipped,
    int TagStripped = 0,
    string? Error = null)
{
    public bool IsError => Error is not null;

    public static ImportReport Fail(string error) => new(0, 0, 0, 0, error);
}

/// <summary>SPEC §19 / §25.3: one implementation per format (TMX, XLSX).</summary>
public interface ITmExchange
{
    Task<ImportReport> ImportAsync(string path, TmStore tm, CancellationToken ct);
    Task ExportAsync(string path, TmStore tm, CancellationToken ct);
}

/// <summary>SPEC §19 / §25.3: one implementation per format (TBX, XLSX).</summary>
public interface ITbExchange
{
    Task<ImportReport> ImportAsync(string path, TbStore tb, CancellationToken ct);
    Task ExportAsync(string path, TbStore tb, CancellationToken ct);
}