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
/// One image of the chapter strip, ready to render: where its file is, the ORIGINAL
/// dimensions the model uses for coordinates, and where it sits on the strip (SPEC §5.6).
/// </summary>
public sealed record StripSegment(string Path, int Width, int Height, long StripTop);

/// <summary>
/// One drawable bubble marker (D-50). <see cref="Point"/> is in STRIP coordinates;
/// the control converts to screen. A marker has no size — it is drawn as a thick cross.
/// </summary>
public sealed record OverlayMarker(
    string BubbleId,
    Point Point,
    BubbleStatus Status,
    bool IsSelected,
    IReadOnlyList<Point>? PartPoints);

/// <summary>
/// Tiled image control (SPEC §20): wheel scroll, Ctrl+wheel zoom about the cursor,
/// Ctrl+0 / FitWidth; draws resident tiles ±1 margin at the chosen level, upscales the
/// parent coarser tile as a placeholder, gray fallback, FPS overlay. Tiles arrive via
/// <see cref="DecodeQueue"/>, are converted once to WriteableBitmap and kept in a
/// byte-budgeted cache. Never a full-size Avalonia Bitmap.
///
/// <para>The control renders the chapter as ONE CONTINUOUS STRIP (D-49): it holds a
/// pyramid per <see cref="StripSegment"/> and stacks them, so a viewport spanning an image
/// boundary simply draws tiles from two pyramids. There is no page concept and no page
/// switching. All public coordinates are strip coordinates in original pixels.</para>
///
/// <para>Overlays are markers drawn as thick crosses (D-50), sized in screen pixels so they
/// stay legible at any zoom; clicking selects the nearest marker within
/// <see cref="HitRadiusPx"/>.</para>
/// </summary>
public sealed class TiledImageControl : Control
{
    private const long TileBudgetBytes = 384L * 1024 * 1024;
    private const double MinZoom = 0.05;
    private const double MaxZoom = 8.0;

    /// <summary>Cross half-length and stroke width, in SCREEN px (SPEC §14.2).</summary>
    private const double CrossArmPx = 11;
    private const double CrossStrokePx = 3;

    /// <summary>Click tolerance around a marker, in screen px (D-50).</summary>
    public const double HitRadiusPx = 12;

    private sealed class Segment
    {
        public required TileDecoder Decoder { get; init; }
        public required int Width { get; init; }      // ORIGINAL width (strip space)
        public required int Height { get; init; }     // ORIGINAL height (strip space)
        public required long StripTop { get; init; }

        /// <summary>media px per strip px for this image (§5.6).</summary>
        public double MediaScale => Width > 0 ? (double)Decoder.ImageWidth / Width : 1.0;
    }

    private readonly DecodeQueue _queue;
    private readonly Dictionary<(int Seg, TileKey Key), WriteableBitmap> _tiles = [];
    private readonly HashSet<(int Seg, TileKey Key)> _inFlight = [];
    private readonly List<Segment> _segments = [];
    private double _zoom = 1.0;
    private double _offsetX;
    private double _offsetY;
    private string _stripId = "init";
    private long _stripTick;
    private long _budgetUsed;
    private bool _disposed;

    private readonly double[] _frameMs = new double[120];
    private int _frameIndex;
    private long _lastFrameTick;
    private double _fps;

    private List<OverlayMarker> _overlays = [];

    private readonly MarkerInteraction _interaction = new();
    private Point _dragPreviewStrip;

    public TiledImageControl()
    {
        _queue = new DecodeQueue(null);
        _queue.TileReady += OnTileReady;
        _queue.TileFailed += OnTileFailed;
        ClipToBounds = true;
        IsHitTestVisible = true;
    }

    /// <summary>True while Ctrl+B placement mode is armed (SPEC §15.2).</summary>
    public bool PlacementArmed
    {
        get => _interaction.PlacementArmed;
        set
        {
            if (_interaction.PlacementArmed == value)
            {
                return;
            }

            _interaction.PlacementArmed = value;
            Cursor = value ? new Avalonia.Input.Cursor(StandardCursorType.Cross) : null;
        }
    }

    /// <summary>Raised when a placement-mode click drops a new bubble (strip coords, clamped).</summary>
    public event Action<PigComic.Core.Domain.PixelPoint>? PlaceMarkerRequested;

    /// <summary>
    /// Raised when a marker drag commits on mouse-up (SPEC §15.1). <paramref name="partIndex"/>
    /// is 1-based for a part marker, null for the source marker; the point is in strip coords.
    /// </summary>
    public event Action<string, int?, PigComic.Core.Domain.PixelPoint>? MarkerDragCompleted;

    public bool HasImage => _segments.Count > 0;
    public double Zoom => _zoom;
    public double Fps => _fps;

    /// <summary>Strip width in original pixels — the widest image.</summary>
    public int StripWidth { get; private set; }

    /// <summary>Strip height in original pixels — the sum of every image's height.</summary>
    public long StripHeight { get; private set; }

    /// <summary>Raised when a bubble marker is clicked (nearest within <see cref="HitRadiusPx"/>).</summary>
    public event Action<string>? OverlayClicked;

    /// <summary>Raised on PageUp/PageDown: scroll by whole viewports (SPEC §14.2).</summary>
    public event Action<int>? ScrollRequested;

    /// <summary>Raised as the strip scrolls, with the strip Y at the top of the viewport.</summary>
    public event Action<long>? StripPositionChanged;

    /// <summary>
    /// Installs the whole chapter strip. Replaces any previous strip and drops its tiles.
    /// </summary>
    public void SetStrip(IReadOnlyList<StripSegment> segments)
    {
        DisposeSegments();
        DropAllTiles();

        foreach (var s in segments)
        {
            _segments.Add(new Segment
            {
                Decoder = new TileDecoder(s.Path),
                Width = Math.Max(1, s.Width),
                Height = Math.Max(1, s.Height),
                StripTop = s.StripTop,
            });
        }

        StripWidth = _segments.Count == 0 ? 0 : _segments.Max(s => s.Width);
        StripHeight = _segments.Count == 0 ? 0 : _segments.Max(s => s.StripTop + s.Height);

        _zoom = 1.0;
        _offsetX = 0;
        _offsetY = 0;
        _stripId = "strip-" + (++_stripTick);
        _queue.SetPage(_stripId);
        FitWidth();
        InvalidateVisual();
    }

    /// <summary>Single-image convenience for the debug spike window.</summary>
    public void SetImage(string path)
    {
        using var probe = new TileDecoder(path);
        SetStrip([new StripSegment(path, probe.ImageWidth, probe.ImageHeight, 0)]);
    }

    /// <summary>Sets the bubble markers; points are in STRIP coordinates.</summary>
    public void SetOverlays(IReadOnlyList<OverlayMarker> overlays)
    {
        _overlays = [.. overlays];
        InvalidateVisual();
    }

    public void FitWidth()
    {
        if (_segments.Count == 0 || Bounds.Width <= 0 || StripWidth <= 0)
        {
            return;
        }

        _zoom = Bounds.Width / StripWidth;
        _offsetX = 0;
        _offsetY = 0;
        RaiseStripPosition();
        InvalidateVisual();
    }

    private void RaiseStripPosition()
        => StripPositionChanged?.Invoke((long)Math.Max(0, _offsetY / Math.Max(_zoom, 1e-6)));

    private void OnTileReady((string PageId, TileKey Key, SKImage Image) t)
    {
        if (!t.PageId.StartsWith(_stripId, StringComparison.Ordinal) || _disposed)
        {
            t.Image.Dispose();
            return;
        }

        var seg = SegmentIndexFromQueueId(t.PageId);
        WriteableBitmap? wb;
        lock (this)
        {
            _inFlight.Remove((seg, t.Key));
            wb = ToWriteableBitmap(t.Image);
            t.Image.Dispose();
            if (wb is not null)
            {
                Install((seg, t.Key), wb);
            }
        }

        Dispatcher.UIThread.Post(InvalidateVisual);
    }

    private void OnTileFailed((string PageId, TileKey Key, Exception Error) t)
    {
        lock (this)
        {
            _inFlight.Remove((SegmentIndexFromQueueId(t.PageId), t.Key));
        }
    }

    private string QueueId(int segment) => $"{_stripId}#{segment}";

    private static int SegmentIndexFromQueueId(string id)
    {
        var hash = id.LastIndexOf('#');
        return hash >= 0 && int.TryParse(id[(hash + 1)..], out var i) ? i : 0;
    }

    private void Install((int Seg, TileKey Key) key, WriteableBitmap wb)
    {
        var size = (long)wb.PixelSize.Width * wb.PixelSize.Height * 4;
        if (_tiles.Remove(key, out var old))
        {
            // Read the old bitmap's metrics BEFORE disposing it — a disposed
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

    private void DisposeSegments()
    {
        foreach (var s in _segments)
        {
            s.Decoder.Dispose();
        }

        _segments.Clear();
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
        if (_segments.Count == 0)
        {
            return;
        }

        var strip = ScreenToStrip(e.GetPosition(this));
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (InteractionPointerPressed(strip, shift))
        {
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_interaction.Drag is not null)
        {
            InteractionPointerMoved(ScreenToStrip(e.GetPosition(this)));
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_interaction.Drag is not null)
        {
            InteractionPointerReleased(ScreenToStrip(e.GetPosition(this)));
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    /// <summary>Screen point → strip coordinates.</summary>
    private Point ScreenToStrip(Point screen)
        => new((screen.X + _offsetX) / _zoom, (screen.Y + _offsetY) / _zoom);

    /// <summary>Strip point → screen point.</summary>
    public Point StripToScreen(Point strip)
        => new(strip.X * _zoom - _offsetX, strip.Y * _zoom - _offsetY);

    /// <summary>
    /// M6 interaction entry points (internal for the smoke self-check; the pointer
    /// handlers feed them screen→strip-converted positions). Returns true when the
    /// press consumed the event.
    /// </summary>
    internal bool InteractionPointerPressed(Point strip, bool shiftHeld)
    {
        if (_interaction.PlacementArmed)
        {
            // SPEC §15.2: "one click on the strip". Off-strip clicks (the gray border when
            // zoomed out) are ignored — no bubble, stay armed — instead of clamping to the
            // edge and dropping a misleading edge bubble.
            if (StripWidth <= 0 || strip.X < 0 || strip.X >= StripWidth ||
                strip.Y < 0 || strip.Y >= StripHeight)
            {
                return false;
            }

            PlaceMarkerRequested?.Invoke(ToPixel(strip));
            if (!shiftHeld)
            {
                PlacementArmed = false;
            }

            return true;
        }

        var grab = HitTest(strip);
        if (grab.Kind == MarkerInteraction.GrabKind.None)
        {
            return false;
        }

        var marker = OverlayById(grab.BubbleId);
        if (marker is null)
        {
            return false;
        }

        var original = grab.Kind == MarkerInteraction.GrabKind.Source
            ? marker.Point
            : marker.PartPoints![grab.PartIndex];
        _interaction.Drag = new MarkerInteraction.DragState(
            grab.BubbleId, grab.Kind == MarkerInteraction.GrabKind.Part ? grab.PartIndex + 1 : null,
            original, strip - original);
        _dragPreviewStrip = original;
        OverlayClicked?.Invoke(grab.BubbleId); // press also selects (SPEC §14.2)
        InvalidateVisual();
        return true;
    }

    internal void InteractionPointerMoved(Point strip)
    {
        if (_interaction.Drag is not { } drag)
        {
            return;
        }

        _dragPreviewStrip = ClampPoint(strip - drag.GrabOffsetStrip);
        InvalidateVisual();
    }

    internal void InteractionPointerReleased(Point strip)
    {
        if (_interaction.Drag is not { } drag)
        {
            return;
        }

        InteractionPointerMoved(strip);
        _interaction.Drag = null;
        if (_dragPreviewStrip != drag.OriginalStrip)
        {
            MarkerDragCompleted?.Invoke(drag.BubbleId, drag.PartIndex, ToPixel(_dragPreviewStrip));
        }

        InvalidateVisual();
    }

    /// <summary>Esc: cancels an in-flight drag and disarms placement (SPEC §14.6).</summary>
    public bool CancelInteraction()
    {
        if (!_interaction.AnyActive)
        {
            return false;
        }

        _interaction.Cancel();
        Cursor = null;
        InvalidateVisual();
        return true;
    }

    /// <summary>Nearest marker (source or, for a selected bubble, part) within the hit radius.</summary>
    private MarkerInteraction.Grab HitTest(Point strip)
    {
        var radius = HitRadiusPx / _zoom;
        MarkerInteraction.Grab? best = null;
        var bestDistance = double.MaxValue;

        foreach (var overlay in _overlays)
        {
            // Source marker first: when a part marker coincides with it (part 1 sits on
            // the source marker, D-18), the strict-less comparison keeps the source grab.
            var sd = Distance(overlay.Point, strip);
            if (sd <= radius && sd < bestDistance)
            {
                bestDistance = sd;
                best = new MarkerInteraction.Grab(MarkerInteraction.GrabKind.Source, overlay.BubbleId, -1);
            }

            if (!overlay.IsSelected || overlay.PartPoints is null)
            {
                continue;
            }

            for (var i = 0; i < overlay.PartPoints.Count; i++)
            {
                var pd = Distance(overlay.PartPoints[i], strip);
                if (pd <= radius && pd < bestDistance)
                {
                    bestDistance = pd;
                    best = new MarkerInteraction.Grab(MarkerInteraction.GrabKind.Part, overlay.BubbleId, i);
                }
            }
        }

        return best ?? MarkerInteraction.Grab.Empty;
    }

    private OverlayMarker? OverlayById(string bubbleId)
        => _overlays.FirstOrDefault(o => o.BubbleId == bubbleId);

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>Clamps to the strip: 0 ≤ x &lt; StripWidth, 0 ≤ y &lt; StripHeight (SPEC §15.1).</summary>
    private Point ClampPoint(Point strip)
        => new(
            Math.Clamp(strip.X, 0, Math.Max(0, StripWidth - 1)),
            Math.Clamp(strip.Y, 0, Math.Max(0, StripHeight - 1)));

    private static PigComic.Core.Domain.PixelPoint ToPixel(Point strip)
        => new((int)Math.Round(strip.X), (int)Math.Round(strip.Y));

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_segments.Count == 0)
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
            ClampOffsets();
            RaiseStripPosition();
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
        ClampOffsets();
        RaiseStripPosition();
        InvalidateVisual();
    }

    private void ClampOffsets()
    {
        var maxX = Math.Max(0, StripWidth * _zoom - Bounds.Width);
        var maxY = Math.Max(0, StripHeight * _zoom - Bounds.Height);
        _offsetX = Math.Clamp(_offsetX, 0, maxX);
        _offsetY = Math.Clamp(_offsetY, 0, maxY);
    }

    /// <summary>Scrolls by whole viewports (PageUp/PageDown).</summary>
    public void ScrollByViewports(int delta)
    {
        _offsetY += delta * Bounds.Height;
        ClampOffsets();
        RaiseStripPosition();
        InvalidateVisual();
    }

    public void ScrollToStart()
    {
        _offsetY = 0;
        ClampOffsets();
        RaiseStripPosition();
        InvalidateVisual();
    }

    public void ScrollToEnd()
    {
        _offsetY = double.MaxValue;
        ClampOffsets();
        RaiseStripPosition();
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
                ScrollRequested?.Invoke(-1);
                e.Handled = true;
                break;
            case Key.PageDown:
                ScrollRequested?.Invoke(1);
                e.Handled = true;
                break;
            case Key.Home:
                ScrollToStart();
                e.Handled = true;
                break;
            case Key.End:
                ScrollToEnd();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Scrolls the given STRIP point into view if it is off-screen (SPEC §14.2), centring
    /// vertically; horizontal is only touched when the point is off to a side.
    /// </summary>
    public void CenterOn(Point stripPoint)
    {
        if (_segments.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var screen = StripToScreen(stripPoint);
        const double margin = 24.0;
        var insideX = screen.X >= margin && screen.X <= Bounds.Width - margin;
        var insideY = screen.Y >= margin && screen.Y <= Bounds.Height - margin;
        if (insideX && insideY)
        {
            return;
        }

        if (!insideY)
        {
            _offsetY = (stripPoint.Y * _zoom) - (Bounds.Height / 2);
        }

        if (!insideX)
        {
            _offsetX = (stripPoint.X * _zoom) - (Bounds.Width / 2);
        }

        ClampOffsets();
        RaiseStripPosition();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        RecordFrame();
        base.Render(context);
        if (_segments.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            context.FillRectangle(Brushes.Gray, Bounds);
            return;
        }

        // Full-bounds gray backdrop: the strip tiles draw on top, but areas outside the
        // strip (when zoomed out) or awaiting decode are opaque gray. This also makes the
        // whole control hit-testable so placement (Ctrl+B) works on gray areas, not just on
        // decoded tiles — "click on gray does nothing" was the report.
        context.FillRectangle(Brushes.Gray, Bounds);

        // Viewport in strip coordinates.
        var viewTop = _offsetY / _zoom;
        var viewBottom = (_offsetY + Bounds.Height) / _zoom;

        for (var i = 0; i < _segments.Count; i++)
        {
            var seg = _segments[i];
            if (seg.StripTop + seg.Height <= viewTop || seg.StripTop >= viewBottom)
            {
                continue;   // entirely outside the viewport
            }

            RenderSegment(context, i, seg);
        }

        DrawOverlays(context);
        DrawFps(context);
    }

    private void RenderSegment(DrawingContext context, int index, Segment seg)
    {
        // This image's own media-pixel space: strip zoom scaled by its media scale.
        var mediaZoom = _zoom / seg.MediaScale;
        var imgW = seg.Decoder.ImageWidth;
        var imgH = seg.Decoder.ImageHeight;
        if (imgW <= 0 || imgH <= 0)
        {
            return;
        }

        var level = TilePyramid.SelectLevel(imgW, imgH, mediaZoom);

        // Where this image's top-left sits on screen.
        var originX = -_offsetX;
        var originY = (seg.StripTop * _zoom) - _offsetY;

        var viewport = (
            -originX / mediaZoom,
            -originY / mediaZoom,
            Bounds.Width / mediaZoom,
            Bounds.Height / mediaZoom);
        var tiles = TilePyramid.VisibleTilesAtLevel(imgW, imgH, level, viewport, margin: 1);

        foreach (var tile in tiles)
        {
            var scale = TilePyramid.LevelScale(level);
            var srcX = tile.Col * TilePyramid.TileSize * scale;
            var srcY = tile.Row * TilePyramid.TileSize * scale;
            var srcW = Math.Min(TilePyramid.TileSize * scale, imgW - srcX);
            var srcH = Math.Min(TilePyramid.TileSize * scale, imgH - srcY);

            var dst = new Rect(
                originX + (srcX * mediaZoom),
                originY + (srcY * mediaZoom),
                srcW * mediaZoom,
                srcH * mediaZoom);

            if (dst.X >= Bounds.Width || dst.Y >= Bounds.Height ||
                dst.X + dst.Width <= 0 || dst.Y + dst.Height <= 0)
            {
                continue;
            }

            var key = (index, tile);
            if (_tiles.TryGetValue(key, out var bmp))
            {
                context.DrawImage(bmp, dst);
            }
            else if (!TryCoarsePlaceholder(context, index, tile, dst))
            {
                context.FillRectangle(Brushes.Gray, dst);
            }

            lock (this)
            {
                if (!_inFlight.Add(key))
                {
                    continue;
                }
            }

            var decoder = seg.Decoder;
            _queue.Submit(QueueId(index), tile, Priority(tile), _ => decoder.DecodeTile(tile, CancellationToken.None));
        }
    }

    /// <summary>SPEC §14.2 overlay layer: a thick cross per marker (D-50).</summary>
    private void DrawOverlays(DrawingContext context)
    {
        var drag = _interaction.Drag;
        foreach (var overlay in _overlays)
        {
            var point = overlay.Point;
            var parts = overlay.PartPoints;
            if (drag is not null && drag.BubbleId == overlay.BubbleId)
            {
                if (drag.PartIndex is null)
                {
                    point = _dragPreviewStrip; // dragging the source marker
                }
                else if (parts is not null)
                {
                    var pi = drag.PartIndex.Value - 1;
                    var preview = parts.ToList();
                    preview[pi] = _dragPreviewStrip;
                    parts = preview;
                }
            }

            var screen = StripToScreen(point);
            if (screen.X < -CrossArmPx || screen.Y < -CrossArmPx ||
                screen.X > Bounds.Width + CrossArmPx || screen.Y > Bounds.Height + CrossArmPx)
            {
                continue;
            }

            var color = UiPalette.StatusColor(overlay.Status);
            DrawCross(context, screen, color, overlay.IsSelected);

            if (overlay.IsSelected && parts is not null)
            {
                // D-18: part 1's marker coincides with the source marker — don't draw it
                // again (a single-part bubble shows ONE cross, an N-part bubble shows N).
                for (var i = 1; i < parts.Count; i++)
                {
                    DrawCross(context, StripToScreen(parts[i]), color, selected: false, arm: CrossArmPx * 0.7, stroke: 1.5);
                }
            }
        }
    }

    private static void DrawCross(
        DrawingContext context, Point at, Color color, bool selected,
        double arm = CrossArmPx, double stroke = CrossStrokePx)
    {
        // A dark halo first so the cross stays visible over busy art.
        var halo = new Pen(new SolidColorBrush(Colors.Black, 0.55), stroke + 2, lineCap: PenLineCap.Round);
        var pen = new Pen(new SolidColorBrush(color), selected ? stroke + 1 : stroke, lineCap: PenLineCap.Round);
        var a = selected ? arm * 1.25 : arm;

        foreach (var p in new[] { halo, pen })
        {
            context.DrawLine(p, new Point(at.X - a, at.Y), new Point(at.X + a, at.Y));
            context.DrawLine(p, new Point(at.X, at.Y - a), new Point(at.X, at.Y + a));
        }
    }

    private bool TryCoarsePlaceholder(DrawingContext context, int segment, TileKey tile, Rect dst)
    {
        if (tile.Level <= 0)
        {
            return false;
        }

        var parent = new TileKey(tile.Level - 1, tile.Col >> 1, tile.Row >> 1);
        if (!_tiles.TryGetValue((segment, parent), out var coarse))
        {
            return false;
        }

        context.DrawImage(coarse, dst);
        return true;
    }

    private static double Priority(TileKey key) => (key.Row * 1e6) + key.Col;

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
        DisposeSegments();
    }
}
