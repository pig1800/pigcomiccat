namespace PigComic.Core.Imaging;

/// <summary>
/// SPEC §20 pyramid math. Level 0 = full resolution; level k halves each
/// dimension. Generation stops once max(width, height) ≤ 1024, but at least 2
/// downsampled levels are always present. Tile size 512×512 (edge tiles smaller).
/// </summary>
public static class TilePyramid
{
    public const int TileSize = 512;

    /// <summary>Number of levels (level indices 0..LevelCount-1) for the given dimensions.</summary>
    public static int LevelCount(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
        }

        var levels = 1;
        var w = width;
        var h = height;
        while (Math.Max(w, h) > 1024 || levels <= 2)
        {
            w = (w + 1) / 2;
            h = (h + 1) / 2;
            levels++;
        }

        return levels;
    }

    /// <summary>Pixel dimensions of one level (ceil of original / 2^k).</summary>
    public static (int Width, int Height) LevelDimensions(int width, int height, int level)
    {
        if (level < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        return (CeilDiv(width, 1 << level), CeilDiv(height, 1 << level));
    }

    /// <summary>Display scale of a level relative to the original (1 / 2^level).</summary>
    public static double LevelScale(int level) => 1.0 / (1 << level);

    /// <summary>Tile grid (cols × rows) for a level.</summary>
    public static (int Cols, int Rows) TileGrid(int width, int height, int level)
    {
        var (lw, lh) = LevelDimensions(width, height, level);
        return (CeilDiv(lw, TileSize), CeilDiv(lh, TileSize));
    }

    /// <summary>
    /// Smallest level whose scale is ≥ <paramref name="zoom"/> (SPEC §20);
    /// zoom 1 = fit level 0 at original size. Scales are exact powers of 0.5,
    /// so the comparison is floating-point safe.
    /// </summary>
    public static int SelectLevel(int width, int height, double zoom)
    {
        var maxLevel = LevelCount(width, height) - 1;
        var level = 0;
        while (level < maxLevel && LevelScale(level + 1) >= zoom)
        {
            level++;
        }

        return level;
    }

    /// <summary>
    /// Tiles intersecting the viewport at the chosen level, expanded by
    /// <paramref name="margin"/> tiles on every side. <paramref name="viewport"/>
    /// is in original-image pixel coordinates; zoom maps it to the level grid.
    /// </summary>
    public static IReadOnlyList<TileKey> VisibleTiles(
        int width, int height, double zoom,
        (double X, double Y, double W, double H) viewport, int margin = 1)
    {
        var level = SelectLevel(width, height, zoom);
        return VisibleTilesAtLevel(width, height, level, viewport, margin);
    }

    /// <summary>Same as <see cref="VisibleTiles"/> but for an explicit level.</summary>
    public static IReadOnlyList<TileKey> VisibleTilesAtLevel(
        int width, int height, int level,
        (double X, double Y, double W, double H) viewport, int margin = 1)
    {
        var scale = LevelScale(level);
        var (cols, rows) = TileGrid(width, height, level);

        double lx = viewport.X * scale;
        double ly = viewport.Y * scale;
        double lw = viewport.W * scale;
        double lh = viewport.H * scale;

        var colMin = Math.Max(0, (int)Math.Floor(lx / TileSize) - margin);
        var colMax = Math.Min(cols - 1, (int)Math.Floor((lx + lw) / TileSize) + margin);
        var rowMin = Math.Max(0, (int)Math.Floor(ly / TileSize) - margin);
        var rowMax = Math.Min(rows - 1, (int)Math.Floor((ly + lh) / TileSize) + margin);

        var tiles = new List<TileKey>();
        for (var r = rowMin; r <= rowMax; r++)
        {
            for (var c = colMin; c <= colMax; c++)
            {
                tiles.Add(new TileKey(level, c, r));
            }
        }

        return tiles;
    }

    public static int CeilDiv(int a, int b) => (a + b - 1) / b;
}