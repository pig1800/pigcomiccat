using System.Runtime.InteropServices;
using PigComic.Core.Imaging;
using SkiaSharp;

namespace PigComic.App.Rendering;

/// <summary>
/// M2.3 tile decoder. Tries true region decode first (JPEG subset via
/// SKCodecOptions.Subset; PNG scanline bands); SkiaSharp's bundled codecs report
/// "Unimplemented" for both, so per SPEC §20's sanctioned fallback the decoder
/// does one full decode and slices tiles from it — only for images whose full
/// RGBA size ≤ 256 MB (verified, see DECISIONS entry at the spike). The
/// in-memory full bitmap lives inside this instance until disposed.
/// </summary>
public sealed class TileDecoder : IDisposable
{
    private readonly object _sync = new();
    private SKBitmap? _full;
    private SKCodec? _codec;
    private SKFileStream? _stream;
    private bool _disposed;

    /// <summary>Number of full-image decodes performed (0 when region decode works).</summary>
    public int FullDecodeCount { get; private set; }

    public int ImageWidth { get; private set; }
    public int ImageHeight { get; private set; }

    /// <summary>Bytes of a full RGBA8888 decode of this image.</summary>
    public long FullRgbaBytes => (long)ImageWidth * ImageHeight * 4;

    public string Path { get; }

    public TileDecoder(string path)
    {
        Path = path;
        EnsureCodec();
        ImageWidth = _codec!.Info.Width;
        ImageHeight = _codec.Info.Height;
    }

    private void EnsureCodec()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TileDecoder));
        }

        if (_codec is not null)
        {
            return;
        }

        _stream = new SKFileStream(Path);
        _codec = SKCodec.Create(_stream)
            ?? throw new InvalidDataException($"Cannot decode {Path}");
    }

    /// <summary>
    /// Decodes one tile (edge tiles smaller than 512). The returned SKImage must
    /// be disposed by the caller (or held by a cache).
    /// </summary>
    public SKImage DecodeTile(TileKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (cols, rows) = TilePyramid.TileGrid(ImageWidth, ImageHeight, key.Level);
        if (key.Col >= cols || key.Row >= rows)
        {
            throw new ArgumentOutOfRangeException(nameof(key), "Tile outside image grid.");
        }

        var scale = TilePyramid.LevelScale(key.Level);
        var srcX = (int)(key.Col * TilePyramid.TileSize * scale);
        var srcY = (int)(key.Row * TilePyramid.TileSize * scale);
        var srcW = (int)Math.Min(TilePyramid.TileSize * scale, ImageWidth - srcX);
        var srcH = (int)Math.Min(TilePyramid.TileSize * scale, ImageHeight - srcY);
        var outWidth = (int)Math.Ceiling(srcW / scale);
        var outHeight = (int)Math.Ceiling(srcH / scale);

        var region = RegionImage(srcX, srcY, srcW, srcH, ct);
        if (key.Level == 0)
        {
            return region;
        }

        using var surface = SKSurface.Create(new SKImageInfo(outWidth, outHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium };
        surface.Canvas.DrawImage(region, new SKRect(0, 0, outWidth, outHeight), paint);
        region.Dispose();
        return surface.Snapshot();
    }

/// <summary>
    /// Always-sliced region path: keeps a lazy full decode and crops from it
    /// **under the decoder lock** so a concurrent Dispose (page switch) cannot
    /// free the native bitmap mid-draw (AccessViolation). Returns an independently
    /// owned SKImage snapshot.
    /// </summary>
    private SKImage RegionImage(int x, int y, int w, int h, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TileDecoder));
            }

            var src = FullImageLocked(ct);
            using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
            // DrawBitmap reads the shared _full — must stay inside the lock so
            // Dispose cannot free it until the copy is done.
            surface.Canvas.DrawBitmap(src, new SKRect(x, y, x + w, y + h), new SKRect(0, 0, w, h));
            return surface.Snapshot();
        }
    }

    /// <summary>First full decode is done once (spec-bounded: RGBA ≤ 256 MB). Caller holds <see cref="_sync"/>.</summary>
    private SKBitmap FullImageLocked(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_full is not null)
        {
            return _full;
        }

        EnsureCodec();
        var info = _codec!.Info;
        long rgba = (long)info.Width * info.Height * 4;
        if (rgba > 256L * 1024 * 1024)
        {
            throw new InvalidOperationException(
                $"Image too large for the spike fallback path ({rgba} bytes RGBA); " +
                "a true subset decoder is required.");
        }

        var bmp = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var result = _codec.GetPixels(bmp.Info, bmp.GetPixels());
        if (result != SKCodecResult.Success)
        {
            bmp.Dispose();
            throw new InvalidDataException($"Decode failed: {result}.");
        }

        _full = bmp;
        FullDecodeCount++;
        return _full;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _full?.Dispose();
            _full = null;
            _codec?.Dispose();
            _codec = null;
            _stream?.Dispose();
            _stream = null;
        }
    }
}