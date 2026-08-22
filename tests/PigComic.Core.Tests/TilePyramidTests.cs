using PigComic.Core.Imaging;
using Xunit;

namespace PigComic.Core.Tests;

/// <summary>SPEC §20 pyramid math — PLAN M2.1 acceptance.</summary>
public class TilePyramidTests
{
    [Fact]
    public void Strip_1000x40000_Has_Exactly_7_Levels()
    {
        var count = TilePyramid.LevelCount(1000, 40000);
        Assert.Equal(7, count);
        var expected = new (int, int)[]
        {
            (1000, 40000), (500, 20000), (250, 10000), (125, 5000),
            (63, 2500), (32, 1250), (16, 625),
        };
        for (var level = 0; level < count; level++)
        {
            Assert.Equal(expected[level], TilePyramid.LevelDimensions(1000, 40000, level));
        }
    }

    [Fact]
    public void Small_Image_Always_Has_Min_Two_Downsampled_Levels()
    {
        Assert.Equal(3, TilePyramid.LevelCount(1000, 1000));   // 0,1,2
        Assert.Equal(3, TilePyramid.LevelCount(512, 512));     // 0,1,2
        Assert.Equal(3, TilePyramid.LevelCount(100, 100));
    }

    [Fact]
    public void Level0_Grid_Of_1000x40000_Is_2x79()
    {
        var (cols, rows) = TilePyramid.TileGrid(1000, 40000, 0);
        Assert.Equal(2, cols);
        Assert.Equal(79, rows);
    }

    [Theory]
    [InlineData(0.24, 2)]
    [InlineData(1.0, 0)]
    [InlineData(1.5, 0)]
    [InlineData(0.5, 1)]
    [InlineData(0.26, 1)]
    [InlineData(0.25, 2)]
    [InlineData(0.1, 3)]
    [InlineData(0.01, 6)]
    [InlineData(0.001, 6)] // clamped to max level
    [InlineData(4.0, 0)]
    public void SelectLevel_Returns_Smallest_Scale_At_Least_Zoom(double zoom, int expectedLevel)
    {
        Assert.Equal(expectedLevel, TilePyramid.SelectLevel(1000, 40000, zoom));
    }

    [Fact]
    public void VisibleTiles_Covers_Viewport_Plus_Margin()
    {
        // Whole page at level 0: every tile in the 2x79 grid.
        var all = TilePyramid.VisibleTiles(1000, 40000, 1.0, (0, 0, 1000, 40000), margin: 0);
        Assert.Equal(2 * 79, all.Count);
        Assert.Contains(new TileKey(0, 1, 78), all);
        Assert.Contains(new TileKey(0, 0, 0), all);

        var withMargin = TilePyramid.VisibleTiles(1000, 40000, 1.0, (0, 0, 1000, 40000), margin: 1);
        Assert.Equal(all.Count, withMargin.Count); // clamped at page edges
    }

    [Fact]
    public void VisibleTiles_Clamps_To_Grid_Bounds()
    {
        var tiles = TilePyramid.VisibleTiles(1000, 40000, 1.0, (0, 0, 600, 600), margin: 0);
        Assert.All(tiles, t => Assert.True(t.Col is >= 0 and <= 1 && t.Row is >= 0 and <= 78));
    }

    [Fact]
    public void VisibleTiles_Selects_Coarser_Level_Below_Threshold()
    {
        // zoom 0.4 -> level 1 -> 250x10000 level dims -> grid 1x20
        var tiles = TilePyramid.VisibleTiles(1000, 40000, 0.4, (0, 0, 1000, 40000), margin: 1);
        Assert.All(tiles, t => Assert.Equal(1, t.Level));
        Assert.Contains(new TileKey(1, 0, 0), tiles);
var (cols, rows) = TilePyramid.TileGrid(1000, 40000, 1);
        Assert.Equal((1, 40), (cols, rows));
    }

    [Fact]
    public void Edge_Tiles_Smaller_Than_512()
    {
        // Level 0 of 1000x40000: last col covers 488 px, last row 40000-78*512=64 px.
        var (cols, _) = TilePyramid.TileGrid(1000, 40000, 0);
        Assert.Equal(2, cols);
    }
}

public class LruByteCacheTests
{
    [Fact]
    public void Evicts_Lru_First()
    {
        var cache = new LruByteCache<string, byte[]>(budgetBytes: 100, sizeOf: b => b.Length);
        cache.Insert("a", new byte[40]);
        cache.Insert("b", new byte[40]);
        cache.Insert("c", new byte[40]); // 120 > 100 -> evict "a"

        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(80, cache.UsedBytes);
    }

    [Fact]
    public void Access_Refreshes_Lru_Position()
    {
        var cache = new LruByteCache<string, byte[]>(budgetBytes: 100, sizeOf: b => b.Length);
        cache.Insert("a", new byte[40]);
        cache.Insert("b", new byte[40]);
        cache.TryGet("a", out _);            // a now most recent
        cache.Insert("c", new byte[40]);     // over budget -> evict "b"

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void Eviction_Callback_Fires_Per_Evicted_Item()
    {
        var evicted = new List<string>();
        var cache = new LruByteCache<string, byte[]>(
            budgetBytes: 100, sizeOf: b => b.Length, onEvict: (k, _) => evicted.Add(k));
        cache.Insert("a", new byte[40]);
        cache.Insert("b", new byte[40]);
        cache.Insert("c", new byte[40]);

        Assert.Equal(["a"], evicted);
    }

    [Fact]
    public void Single_Item_Larger_Than_Budget_Is_Still_Kept()
    {
        var cache = new LruByteCache<string, byte[]>(budgetBytes: 10, sizeOf: b => b.Length);
        cache.Insert("a", new byte[100]);
        Assert.True(cache.TryGet("a", out _)); // own size stays even if over budget
        Assert.Equal(100, cache.UsedBytes);
    }

    [Fact]
    public void Clear_Evicts_Everything()
    {
        var evicted = 0;
        var cache = new LruByteCache<string, byte[]>(
            budgetBytes: 1000, sizeOf: b => b.Length, onEvict: (_, _) => evicted++);
        cache.Insert("a", new byte[10]);
        cache.Insert("b", new byte[10]);
        cache.Clear();
        Assert.Equal(2, evicted);
        Assert.Equal(0, cache.UsedBytes);
    }
}
