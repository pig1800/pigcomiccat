using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PigComic.Core.Package;

/// <summary>
/// Atomic package write (SPEC §5.5): temp archive in the same directory,
/// stream-copied media entries (no compression, verbatim bytes), then
/// <c>File.Replace</c> with the previous version kept as <c>.bak</c>.
/// </summary>
public static class AtomicZipWriter
{
    /// <summary>
    /// Writes a complete new archive at <paramref name="tmpPath"/>:
    /// <c>content.xml</c> deflated from the serialized XML; every other entry of
    /// <paramref name="sourceArchive"/> stream-copied uncompressed and verbatim.
    /// </summary>
    public static void WriteTemp(
        string tmpPath,
        ZipArchive sourceArchive,
        byte[] contentXmlBytes,
        Action<ZipArchive>? midWriteHook = null)
    {
        using var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var content = zip.CreateEntry("content.xml", CompressionLevel.Optimal);
        using (var s = content.Open())
        {
            s.Write(contentXmlBytes, 0, contentXmlBytes.Length);
        }

        midWriteHook?.Invoke(zip);

        foreach (var entry in sourceArchive.Entries)
        {
            if (string.Equals(entry.FullName, "content.xml", StringComparison.Ordinal))
            {
                continue;
            }

            var newEntry = zip.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
            using var src = entry.Open();
            using var dst = newEntry.Open();
            src.CopyTo(dst);
        }
    }

    /// <summary>
    /// Replaces <paramref name="targetPath"/> with the temp file; the previous
    /// version survives as <paramref name="backupPath"/> (one generation, D-05).
    /// Retries <c>File.Replace</c> once; falls back to copy+delete+move when the
    /// platform or a concurrent handle prevents it (SPEC §5.5 step 4).
    /// </summary>
    public static void Commit(string tmpPath, string targetPath, string backupPath)
    {
        var replaced = false;
        for (var attempt = 0; attempt < 2 && !replaced; attempt++)
        {
            try
            {
                File.Replace(tmpPath, targetPath, backupPath);
                replaced = true;
            }
            catch (PlatformNotSupportedException)
            {
                break;
            }
            catch (IOException)
            {
                Thread.Sleep(30); // transient lock; retry once, then fall back
            }
        }

        if (replaced)
        {
            return;
        }

        // SPEC §5.5 step 4 fallback: copy target->bak, then swap.
        File.Copy(targetPath, backupPath, overwrite: true);
        File.Delete(targetPath);
        File.Move(tmpPath, targetPath);
    }

    /// <summary>UTF-8 without BOM, LF newlines (SPEC §5.3).</summary>
    public static byte[] SerializeContentXml(XDocument content)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            CloseOutput = false,
        };

        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, settings))
        {
            content.Save(writer);
        }

        return ms.ToArray();
    }
}