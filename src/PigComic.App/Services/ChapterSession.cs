using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PigComic.Core.Package;

namespace PigComic.App.Services;

/// <summary>
/// M5.1 editor chapter session. Owns the open <see cref="PcmlDocument"/> (the
/// XDocument persistence model, SPEC §5.8), the dirty flag and the save path
/// (delegating to the Core atomic save, §5.5). Also owns the crash-journal
/// case (§23 — Discard-only until M9.2's Recover dialog) and materialises
/// page media into a per-chapter temp cache so <c>TiledImageControl</c>
/// (which decodes from a file path) can render it (§5.6 media handling).
/// </summary>
public sealed class ChapterSession : IDisposable
{
    private readonly Dictionary<string, string> _mediaCache = new(StringComparer.Ordinal);
    private string? _mediaDir;

    private ChapterSession(PcmlDocument document) => Document = document;

    public PcmlDocument Document { get; }

    public bool IsDirty { get; private set; }

    /// <summary>Raised when the dirty flag changes (status bar).</summary>
    public event Action? DirtyChanged;

    public string JournalPath => Document.JournalPath;

    public bool HasJournal => File.Exists(JournalPath);

    /// <summary>Opens the package; validation (SPEC §5.7) runs inside Core.</summary>
    public static Task<ChapterSession> OpenAsync(string path, CancellationToken ct)
        => Task.Run(() => new ChapterSession(PcmlDocument.Open(path)), ct);

    /// <summary>SPEC §23 interim two-button flow: delete the journal.</summary>
    public void DiscardJournal()
    {
        if (File.Exists(JournalPath))
        {
            File.Delete(JournalPath);
        }
    }

    public void MarkDirty()
    {
        if (IsDirty)
        {
            return;
        }

        IsDirty = true;
        DirtyChanged?.Invoke();
    }

    public void ClearDirty()
    {
        if (!IsDirty)
        {
            return;
        }

        IsDirty = false;
        DirtyChanged?.Invoke();
    }

    /// <summary>Atomic save (SPEC §5.5). Throws on read-only documents.</summary>
    public async Task SaveAsync(CancellationToken ct)
    {
        await Document.SaveAsync(ct);
        ClearDirty();
    }

    /// <summary>
    /// Materialises the page's media entry into a temp cache file and returns
    /// its path (TiledImageControl decodes from a path). Reuses an existing
    /// extraction while its size matches the package entry; never writes into
    /// the package itself, so round-trip preservation (§5.8) is untouched.
    /// </summary>
    public string? PageImagePath(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        if (_mediaCache.TryGetValue(fileName, out var cached) && File.Exists(cached))
        {
            return cached;
        }

        var entryName = "media/" + fileName;
        var knownLength = Document.MediaEntries
            .FirstOrDefault(m => string.Equals(m.Name, entryName, StringComparison.OrdinalIgnoreCase))
            ?.Length;

        var dest = Path.Combine(MediaDir(), fileName);
        if (File.Exists(dest) && (knownLength is null || new FileInfo(dest).Length == knownLength))
        {
            _mediaCache[fileName] = dest;
            return dest;
        }

        Directory.CreateDirectory(MediaDir());
        using (var archive = new ZipArchive(
                   new FileStream(Document.Path, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete),
                   ZipArchiveMode.Read))
        {
            var entry = archive.GetEntry(entryName)
                ?? archive.Entries.FirstOrDefault(e =>
                    string.Equals(e.FullName, entryName, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                throw new InvalidOperationException(
                    $"Package has no media entry '{entryName}' (page references '{fileName}').");
            }

            using var src = entry.Open();
            using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
            src.CopyTo(dst);
        }

        _mediaCache[fileName] = dest;
        return dest;
    }

    /// <summary>Per-chapter temp dir: %TEMP%/pigcomic-media/&lt;hash of package path&gt;.</summary>
    private string MediaDir()
    {
        if (_mediaDir is not null)
        {
            return _mediaDir;
        }

        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(Document.Path))))[..8];
        _mediaDir = Path.Combine(Path.GetTempPath(), "pigcomic-media", hash);
        return _mediaDir;
    }

    public void Dispose() => Document.Dispose();
}