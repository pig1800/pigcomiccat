using System.Security.Cryptography;
using System.Text;

namespace PigComic.Core.Tm;

/// <summary>SPEC §7.3: source_hash = first 8 bytes of SHA-256(UTF-8 of normalized) as LE long.</summary>
public static class TmHash
{
    public static long Compute(string normalizedText)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedText));
        return BitConverter.ToInt64(bytes, 0);
    }
}