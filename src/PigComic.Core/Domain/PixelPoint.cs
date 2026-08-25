namespace PigComic.Core.Domain;

/// <summary>
/// A bubble's anchor in STRIP coordinates: top-left origin, X from the strip's left edge,
/// Y from the top of the first image and continuing across image boundaries (SPEC §5.6).
///
/// <para>Deliberately a point and not a rectangle (D-50): comic text is frequently not
/// rectangular — SFX least of all — so a bounding box was both inaccurate about the artwork
/// and extra work to maintain. Everything downstream needs only the anchor.</para>
/// </summary>
public sealed record PixelPoint(int X, int Y)
{
    public static readonly PixelPoint Origin = new(0, 0);

    public bool IsValid => X >= 0 && Y >= 0;
}
