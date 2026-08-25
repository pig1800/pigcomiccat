using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PigComic.App.Rendering;
using PigComic.App.Services;
using PigComic.Core.Domain;
using PigComic.Core.Imaging;
using SkiaSharp;

namespace PigComic.App.Controls;

/// <summary>
/// One drawable overlay rectangle (M5.3). <see cref="Region"/> is in ORIGINAL
/// image pixel space (SPEC §5.6); the control multiplies by the media scale.
/// </summary>
public sealed record OverlayRect(
    string BubbleId,
    Rect Region,
    BubbleStatus Status,
    bool IsSelected,
    IReadOnlyList<Rect>? PartRegions);

/// <summary>
/// M2.4 tiled image control (SPEC §20 spike): wheel scroll, Ctrl+wheel zoom
/// about the cursor, Ctrl+0 / FitWidth; draws resident tiles ±1 margin at the
/// chosen level, upscales the parent coarser tile as a placeholder, gray
/// fallback, FPS overlay (frame-time ring buffer). Tiles arrive via
/// <see cref="DecodeQueue"/>, are converted once to WriteableBitmap and kept in
/// a byte-budgeted cache. Never a full-size Avalonia Bitmap.
///
/// M5.3: also draws bubble overlays (SPEC §14.2) — status-colored outlines,
/// selected fill, dashed part regions — and raises clicks in original space.
/// </summary>
public sealed class TiledImageControl : Control
{
    private const long TileBudgetBytes = 384L * 1024 * 1024;
    private const double MinZoom = 0.05;
    private const double MaxZoom = 8.0;

    private readonly DecodeQueue _queue;
    private readonly Dictionary<TileKey, WriteableBitmap> _tiles = [];
    private readonly HashSet<TileKey> _inFlight = [];
    private TileDecoder? _decoder;
    private double _zoom = 1.0;
    private double _offsetX;
    private double _offsetY;
    private string _pageId = "init";
    private long _pageTick;
    private long _budgetUsed;
    private bool _disposed;

    private readonly double[] _frameMs = new double[120];
    private int _frameIndex;
    private long _lastFrameTick;
    private double _fps;

    public TiledImageControl()
    {
        _queue = new DecodeQueue(null);
        _queue.TileReady += OnTileReady;
        _queue.TileFailed += OnTileFailed;
        ClipToBounds = true;
    }

    public bool HasImage => _decoder is not null;
    public double Zoom => _zoom;
    public double Fps => _fps;

    /// <summary>Actual media width of the current image (used for §5.6 scale).</summary>
    public int ImageWidth => _decoder?.ImageWidth ?? 0;

    /// <summary>Raised when a bubble overlay is clicked (SPEC §14.2 smallest-topmost rule).</summary>
    public event Action<string>? OverlayClicked;

    /// <summary>Raised by PageUp/PageDown when the image pane has focus (SPEC §14.2).</summary>
    public event Action<int>? PageNavigationRequested;

    private List<OverlayRect> _overlays = [];
    private double _overlayScale = 1.0;
    private double _overlayPageWidth = 1.0;

    /// <summary>
    /// Sets the bubble overlays for the current page; rects are in ORIGINAL
    /// pixel space and are drawn scaled by mediaWidth/pageWidth (§5.6).
    /// </summary>
    public void SetOverlays(IReadOnlyList<OverlayRect> overlays, double pageWidth)
    {
        _overlays = [.. overlays];
        _overlayPageWidth = pageWidth > 0 ? pageWidth : ImageWidth > 0 ? ImageWidth : 1.0;
        var mediaWidth = ImageWidth;
        _overlayScale = mediaWidth > 0 ? mediaWidth / _overlayPageWidth : 1.0;
        InvalidateVisual();
    }

    /// <summary>Original-space → control-screen transform for overlay math.</summary>
    private (double X, double Y, double W, double H) OverlayScreen(double x, double y, double w, double h)
    {
        var s = _overlayScale * _zoom;
        return (x * s - _offsetX, y * s - _offsetY, w * s, h * s);
    }

    private Rect OverlayScreen(Rect r)
    {
        var (x, y, w, h) = OverlayScreen(r.X, r.Y, r.Width, r.Height);
        return new Rect(x, y, w, h);
    }

    public void SetImage(string path)
    {
        _decoder?.Dispose();
        _decoder = new TileDecoder(path);
        _zoom = 1.0;
        _offsetX = 0;
        _offsetY = 0;
        _pageId = "page-" + (++_pageTick);
        _queue.SetPage(_pageId);
        DropAllTiles();
        FitWidth();
        InvalidateVisual();
    }

    private void DropAllTiles()
    {
        foreach (var bmp in _tiles.Values)
        {
            bmp.Dispose();
        }

        _tiles.Clear();
        _inFlight.Clear();
        _budgetUsed = 0;
    }

    public void FitWidth()
    {
        if (_decoder is null || Bounds.Width <= 0)
        {
            return;
        }

        _zoom = Bounds.Width / _decoder.ImageWidth;
        _offsetX = 0;
        _offsetY = 0;
        InvalidateVisual();
    }

    private void OnTileReady((string PageId, TileKey Key, SKImage Image) t)
    {
        if (t.PageId != _pageId || _disposed)
        {
            t.Image.Dispose();
            return;
        }

        WriteableBitmap? wb;
        lock (this)
        {
            _inFlight.Remove(t.Key);
            wb = ToWriteableBitmap(t.Image);
            t.Image.Dispose();
            if (wb is not null)
            {
                Install(t.Key, wb);
            }
        }

        Dispatcher.UIThread.Post(InvalidateVisual);
    }

    private void OnTileFailed((string PageId, TileKey Key, Exception Error) t)
    {
        lock (this)
        {
            _inFlight.Remove(t.Key);
        }
    }

    private void Install(TileKey key, WriteableBitmap wb)
    {
        var size = (long)wb.PixelSize.Width * wb.PixelSize.Height * 4;
        if (_tiles.Remove(key, out var old))
        {
            // Read the old bitmap's metrics BEFORE disposing it — a disposable
            // Avalonia Bitmap throws ObjectDisposedException on any access.
            var oldBudget = (long)old.PixelSize.Width * old.PixelSize.Height * 4;
            old.Dispose();
            _budgetUsed -= oldBudget;
        }

        _tiles[key] = wb;
        _budgetUsed += size;
        while (_budgetUsed > TileBudgetBytes && _tiles.Count > 1)
        {
            var victim = _tiles.Keys.First();
            var removed = _tiles[victim];
            _budgetUsed -= (long)removed.PixelSize.Width * removed.PixelSize.Height * 4;
            removed.Dispose();
            _tiles.Remove(victim);
        }
    }

    private static unsafe WriteableBitmap? ToWriteableBitmap(SKImage image)
    {
        var w = image.Width;
        var h = image.Height;
        if (w <= 0 || h <= 0)
        {
            return null;
        }

        var bytes = new byte[w * h * 4];
        var pixInfo = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        var gch = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        bool ok;
        try
        {
            ok = image.ReadPixels(pixInfo, gch.AddrOfPinnedObject(), w * 4);
        }
        finally
        {
            gch.Free();
        }

        if (!ok)
        {
            return null;
        }

        var wb = new WriteableBitmap(
            new PixelSize(w, h), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fbo = wb.Lock();
        var dst = new Span<byte>(fbo.Address.ToPointer(), fbo.RowBytes * h);
        for (var y = 0; y < h; y++)
        {
            var sOff = y * w * 4;
            var dOff = y * fbo.RowBytes;
            for (var x = 0; x < w; x++)
            {
                dst[dOff + x * 4 + 0] = bytes[sOff + x * 4 + 2];
                dst[dOff + x * 4 + 1] = bytes[sOff + x * 4 + 1];
                dst[dOff + x * 4 + 2] = bytes[sOff + x * 4 + 0];
                dst[dOff + x * 4 + 3] = bytes[sOff + x * 4 + 3];
            }
        }

        return wb;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_decoder is null || _overlays.Count == 0)
        {
            return;
        }

        var p = e.GetPosition(this);
        // Screen → ORIGINAL space (§5.6): undo overlay scale + zoom + offset.
        var s = _overlayScale * _zoom;
        if (s <= 0)
        {
            return;
        }

        var ox = (p.X + _offsetX) / s;
        var oy = (p.Y + _offsetY) / s;

        OverlayRect? best = null;
        double bestArea = double.MaxValue;
        foreach (var overlay in _overlays)
        {
            var r = overlay.Region;
            if (ox < r.X || oy < r.Y || ox > r.X + r.Width || oy > r.Y + r.Height)
            {
                continue;
            }

            // Smallest region wins; ties keep the last (topmost = drawn later).
            var area = r.Width * r.Height;
            if (area <= bestArea)
            {
                bestArea = area;
                best = overlay;
            }
        }

        if (best is not null)
        {
            OverlayClicked?.Invoke(best.BubbleId);
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_decoder is null)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var p = e.GetPosition(this);
            ZoomAbout(p.X, p.Y, _zoom * Math.Pow(1.12, e.Delta.Y));
            e.Handled = true;
        }
        else
        {
            _offsetX -= e.Delta.X * 40;
            _offsetY -= e.Delta.Y * 40;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    public void ZoomAbout(double px, double py, double newZoom)
    {
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        var k = newZoom / _zoom;
        _offsetX = px - (px - _offsetX) * k;
        _offsetY = py - (py - _offsetY) * k;
        _zoom = newZoom;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Add:
            case Key.OemPlus:
                ZoomAbout(Bounds.Width / 2, Bounds.Height / 2, _zoom * 1.2);
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                ZoomAbout(Bounds.Width / 2, Bounds.Height / 2, _zoom / 1.2);
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                FitWidth();
                e.Handled = true;
                break;
            case Key.PageUp:
                PageNavigationRequested?.Invoke(-1);
                e.Handled = true;
                break;
            case Key.PageDown:
                PageNavigationRequested?.Invoke(1);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Centers the given ORIGINAL-space region if it is off-screen (SPEC §14.2:
    /// selecting a row auto-scrolls/centers the region; if off-screen vertically,
    /// center it; horizontal is kept unless the region is off-side).
    /// </summary>
    public void CenterOn(Rect originalRegion)
    {
        if (_decoder is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var screen = OverlayScreen(originalRegion);
        var margin = 16.0;
        var insideX = screen.X >= -margin && screen.X + screen.Width <= Bounds.Width + margin;
        var insideY = screen.Y >= -margin && screen.Y + screen.Height <= Bounds.Height + margin;

        if (insideX && insideY)
        {
            return;
        }

        var maxX = Math.Max(0, _decoder.ImageWidth * _zoom - Bounds.Width);
        var maxY = Math.Max(0, _decoder.ImageHeight * _zoom - Bounds.Height);

        if (!insideY)
        {
            var centerY = originalRegion.Y * _overlayScale * _zoom + (originalRegion.Height * _overlayScale * _zoom) / 2;
            _offsetY = Math.Clamp(centerY - Bounds.Height / 2, 0, maxY);
        }

        if (!insideX)
        {
            var centerX = originalRegion.X * _overlayScale * _zoom + (originalRegion.Width * _overlayScale * _zoom) / 2;
            _offsetX = Math.Clamp(centerX - Bounds.Width / 2, 0, maxX);
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        RecordFrame();
        base.Render(context);
        if (_decoder is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            context.FillRectangle(Brushes.Gray, Bounds);
            return;
        }

        var zoom = _zoom;
        var imgW = _decoder.ImageWidth;
        var imgH = _decoder.ImageHeight;
        var level = TilePyramid.SelectLevel(imgW, imgH, zoom);
        var viewport = (_offsetX / zoom, _offsetY / zoom, Bounds.Width / zoom, Bounds.Height / zoom);
        var tiles = TilePyramid.VisibleTilesAtLevel(imgW, imgH, level, viewport, margin: 1);

        foreach (var tile in tiles)
        {
            var rect = TileDisplayRect(tile, zoom, level);
            if (rect.X >= Bounds.Width || rect.Y >= Bounds.Height ||
                rect.X + rect.W <= 0 || rect.Y + rect.H <= 0)
            {
                continue;
            }

            var dst = new Rect(rect.X, rect.Y, rect.W, rect.H);
            if (_tiles.TryGetValue(tile, out var bmp))
            {
                context.DrawImage(bmp, dst);
            }
            else if (!TryCoarsePlaceholder(context, tile, dst))
            {
                context.FillRectangle(Brushes.Gray, dst);
            }

            lock (this)
            {
                if (!_inFlight.Add(tile))
                {
                    continue;
                }
            }

            var decoder = _decoder;
            _queue.Submit(_pageId, tile, Priority(tile), _ => decoder.DecodeTile(tile, CancellationToken.None));
        }

        DrawOverlays(context);
        DrawFps(context);
    }

    /// <summary>SPEC §14.2 overlay layer: status outlines, selected fill + dashed parts.</summary>
    private void DrawOverlays(DrawingContext context)
    {
        if (_decoder is null || _overlays.Count == 0)
        {
            return;
        }

        foreach (var overlay in _overlays)
        {
            var rect = OverlayScreen(overlay.Region);
            if (rect.X >= Bounds.Width || rect.Y >= Bounds.Height ||
                rect.X + rect.Width <= 0 || rect.Y + rect.Height <= 0)
            {
                continue;
            }

            var color = UiPalette.StatusColor(overlay.Status);
            var outline = new Pen(new SolidColorBrush(color), overlay.IsSelected ? 3 : 2);
            if (overlay.IsSelected)
            {
                context.FillRectangle(new SolidColorBrush(color, 0.15f), rect);
            }

            context.DrawRectangle(null, outline, rect);

            if (overlay.IsSelected && overlay.PartRegions is { } parts)
            {
                var dashed = new Pen(new SolidColorBrush(color), 1, new DashStyle([3, 2], 0));
                foreach (var part in parts)
                {
                    var pr = OverlayScreen(part);
                    if (pr.Width > 0 && pr.Height > 0)
                    {
                        context.DrawRectangle(null, dashed, pr);
                    }
                }
            }
        }
    }

    private (double X, double Y, double W, double H) TileDisplayRect(TileKey tile, double zoom, int level)
    {
        var scale = TilePyramid.LevelScale(level);
        var srcX = tile.Col * TilePyramid.TileSize * scale;
        var srcY = tile.Row * TilePyramid.TileSize * scale;
        var srcW = Math.Min(TilePyramid.TileSize * scale, _decoder!.ImageWidth - srcX);
        var srcH = Math.Min(TilePyramid.TileSize * scale, _decoder!.ImageHeight - srcY);
        return (srcX * zoom - _offsetX, srcY * zoom - _offsetY, srcW * zoom, srcH * zoom);
    }

    private bool TryCoarsePlaceholder(DrawingContext context, TileKey tile, Rect dst)
    {
        if (tile.Level <= 0)
        {
            return false;
        }

        var parent = new TileKey(tile.Level - 1, tile.Col >> 1, tile.Row >> 1);
        if (!_tiles.TryGetValue(parent, out var coarse))
        {
            return false;
        }

        context.DrawImage(coarse, dst);
        return true;
    }

    private static double Priority(TileKey key)
    {
        // Called by the caller of Submit with the viewport-derived order already
        // provided by the visible-set ordering; a simple row-major tie break keeps
        // the queue deterministic.
        return key.Row * 1e6 + key.Col;
    }

    private void DrawFps(DrawingContext context)
    {
        var text = new FormattedText(
            $"FPS {_fps:F0}  zoom {_zoom:F2}  tiles {_tiles.Count}",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            13,
            Brushes.Yellow);
        context.DrawText(text, new Point(8, 6));
    }

    private void RecordFrame()
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_lastFrameTick != 0)
        {
            var ms = (now - _lastFrameTick) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _frameMs[_frameIndex] = ms;
            _frameIndex = (_frameIndex + 1) % _frameMs.Length;
            var count = _frameIndex == 0 ? _frameMs.Length : _frameIndex;
            double sum = 0;
            for (var i = 0; i < count; i++)
            {
                sum += _frameMs[i];
            }

            _fps = 1000.0 / (sum / count);
        }

        _lastFrameTick = now;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DropAllTiles();
        _queue.Dispose();
        _decoder?.Dispose();
        _decoder = null;
    }
}