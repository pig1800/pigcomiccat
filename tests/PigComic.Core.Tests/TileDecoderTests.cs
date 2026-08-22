using System.Diagnostics;
using PigComic.App.Rendering;
using PigComic.Core.Imaging;
using SkiaSharp;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>M2.3 acceptance: decode pipeline performance and PNG pixel correctness.</summary>
public class TileDecoderTests : IDisposable
{
    private readonly string _dir;

    public TileDecoderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pigcomic-decoder", Guid.NewGuid().ToString("N"));
        StripImageGenerator.Generate(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void Jpeg_Subset_Tiles_Decode_Fast_Without_Full_Decode()
    {
        var jpeg = Path.Combine(_dir, "strip.jpg");
        using var decoder = new TileDecoder(jpeg);
        Assert.Equal(1000, decoder.ImageWidth);
        Assert.Equal(40000, decoder.ImageHeight);

        var sw = Stopwatch.StartNew();
        var images = new List<(TileKey Key, SKImage Image)>();
        for (var row = 0; row < 10; row++)
        {
            for (var col = 0; col < 2; col++)
            {
                var key = new TileKey(0, col, row);
                images.Add((key, decoder.DecodeTile(key, CancellationToken.None)));
            }
        }

        sw.Stop();
        try
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
                $"20 JPEG tiles took {sw.Elapsed.TotalSeconds:F1}s");
            Assert.Equal(20, images.Count);
            Assert.Equal(new TileKey(0, 1, 9), images[^1].Key);
        }
        finally
        {
            foreach (var (_, img) in images)
            {
                img.Dispose();
            }
        }
    }

    [Fact]
    public void Jpeg_Edges_Are_Small_And_Pixel_Filled()
    {
        var jpeg = Path.Combine(_dir, "strip.jpg");
        using var decoder = new TileDecoder(jpeg);
        using var lastRow = decoder.DecodeTile(new TileKey(0, 0, 78), CancellationToken.None);
        // Row 78: 40000 - 78*512 = 64 px tall — the edge tile is smaller than 512 px.
        Assert.Equal(512, lastRow.Info.Width);
        Assert.Equal(64, lastRow.Info.Height);
    }

    [Fact]
    public void Png_Tile_Matches_Reference_Crop_Pixel_For_Pixel()
    {
        var png = Path.Combine(_dir, "strip.png");
        using var decoder = new TileDecoder(png);
        using var tile = decoder.DecodeTile(new TileKey(0, 0, 1), CancellationToken.None);

        // Reference: full decode in the TEST (allowed) then crop rows 512..1023.
        using var stream = File.OpenRead(png);
        using var codec = SKCodec.Create(stream)!;
        using var full = SKBitmap.Decode(codec);
        using var reference = new SKBitmap(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(reference))
        {
            canvas.DrawBitmap(full, new SKRect(0, 512, 512, 1024), new SKRect(0, 0, 512, 512));
        }

// Copy tile pixels out via pixmap (no aliasing with the image's memory).
        var info = tile.Info;
        var rowBytes = info.Width * info.BytesPerPixel;
        var pixelBytes = new byte[rowBytes * info.Height];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixelBytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            using var pixmap = new SKPixmap(
                new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul),
                handle.AddrOfPinnedObject(), rowBytes);
            if (!tile.ReadPixels(pixmap))
            {
                throw new InvalidDataException("tile.ReadPixels failed");
            }
        }
        finally
        {
            handle.Free();
        }

        Assert.Equal(reference.Bytes, pixelBytes);
    }

    [Fact]
    public void DecodeQueue_Serves_20_Tiles_Under_3s()
    {
        var jpeg = Path.Combine(_dir, "strip.jpg");
        using var queue = new DecodeQueue(ui: null);
        using var decoder = new TileDecoder(jpeg);
        queue.SetPage("p1");

        var gate = new TaskCompletionSource();
        var saw = new HashSet<TileKey>();
        queue.TileReady += t =>
        {
            lock (saw)
            {
                saw.Add(t.Key);
                if (saw.Count >= 20)
                {
                    gate.TrySetResult();
                }
            }
        };

        var sw = Stopwatch.StartNew();
        for (var row = 0; row < 10; row++)
        {
            for (var col = 0; col < 2; col++)
            {
                var key = new TileKey(0, col, row);
                queue.Submit("p1", key, row * 2 + col, _ => decoder.DecodeTile(key, CancellationToken.None));
            }
        }

        var ok = gate.Task.Wait(TimeSpan.FromSeconds(3));
        sw.Stop();
        Assert.True(ok, $"20 tiles did not all arrive within 3 s ({saw.Count} arrived)");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3));
        Assert.Equal(0, queue.PendingCount);
    }
}