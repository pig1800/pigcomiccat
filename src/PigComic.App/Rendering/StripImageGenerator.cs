using SkiaSharp;

namespace PigComic.App.Rendering;

/// <summary>
/// M2.2 spike tool: generates long vertical test strips (1000×40000) with a
/// numbered band pattern ("y=NNNN" every 500 px) as JPEG and PNG. The pattern
/// makes decode errors visually obvious and every band distinguishable.
/// </summary>
public static class StripImageGenerator
{
    public const int DefaultWidth = 1000;
    public const int DefaultHeight = 40000;

    public static void Generate(string outputDir, int width = DefaultWidth, int height = DefaultHeight)
    {
        Directory.CreateDirectory(outputDir);
        var bands = height / 500 + 1;

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 42,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial"),
        };

        for (var b = 0; b < bands; b++)
        {
            var y = b * 500;
            // Distinct hue per band pair (10-band cycle) makes mis-tiling obvious.
            var hue = (b % 10) * 36f;
            var color = SKColor.FromHsl(hue, 60, 75);
            paint.Color = color;
            paint.Style = SKPaintStyle.Fill;
            canvas.DrawRect(new SKRect(0, y, width, Math.Min(height, y + 500)), paint);

            for (var i = 0; i < 5; i++)
            {
                var lineY = y + i * 100;
                paint.Color = SKColors.Red;
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 2;
                canvas.DrawLine(0, lineY, width, lineY, paint);
            }

            var label = $"y={y:00000}";
            textPaint.Color = SKColors.Black;
            canvas.DrawText(label, 30, y + 70, textPaint);
            textPaint.Color = SKColors.White;
            canvas.DrawText(label, 32, y + 72, textPaint);
        }

        using var image = surface.Snapshot();

        using (var jpgData = image.Encode(SKEncodedImageFormat.Jpeg, 85))
        using (var fs = File.Create(Path.Combine(outputDir, "strip.jpg")))
        {
            jpgData.SaveTo(fs);
        }

        using (var pngData = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var fs = File.Create(Path.Combine(outputDir, "strip.png")))
        {
            pngData.SaveTo(fs);
        }
    }

    /// <summary>Validates the produced files: exist, correct dimensions, JPEG under 30 MB.</summary>
    public static (string Jpeg, string Png) Verify(string outputDir, int width, int height)
    {
        var jpeg = Path.Combine(outputDir, "strip.jpg");
        var png = Path.Combine(outputDir, "strip.png");
        if (!File.Exists(jpeg) || !File.Exists(png))
        {
            throw new FileNotFoundException("Generator output missing.");
        }

        var jlen = new FileInfo(jpeg).Length;
        if (jlen >= 30L * 1024 * 1024)
        {
            throw new Exception($"JPEG too large for spike: {jlen} bytes.");
        }

        foreach (var path in new[] { jpeg, png })
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
            {
                throw new Exception($"Cannot decode {path}.");
            }

            if (codec.Info.Width != width || codec.Info.Height != height)
            {
                throw new Exception($"{path}: bad dimensions {codec.Info.Width}x{codec.Info.Height}.");
            }
        }

        return (jpeg, png);
    }
}