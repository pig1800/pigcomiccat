namespace PigComic.Core.Domain;

/// <summary>
/// Axis-aligned rectangle in ORIGINAL image pixel space, top-left origin
/// (SPEC §5.6). Width/Height ≥ 1 for valid regions.
/// </summary>
public sealed record PixelRect(int X, int Y, int Width, int Height)
{
    public bool IsValid => Width >= 1 && Height >= 1;
}