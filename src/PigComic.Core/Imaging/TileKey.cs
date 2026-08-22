namespace PigComic.Core.Imaging;

/// <summary>Identifies one 512×512 (edge tiles smaller) tile of one pyramid level.</summary>
public readonly record struct TileKey(int Level, int Col, int Row)
{
    public override string ToString() => $"L{Level} ({Col},{Row})";
}