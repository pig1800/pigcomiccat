namespace PigComic.Core.Domain;

/// <summary>
/// In-memory view of one .pcml chapter (SPEC §4). Built by the package reader;
/// the XDocument remains the persistence model (SPEC §5.8).
///
/// <para>A chapter has no pages (D-49): its images are joined one after another into a single
/// continuous vertical <b>strip</b>, and every coordinate in the model is a strip coordinate.
/// The images are only the segments the strip is stored in.</para>
/// </summary>
public sealed class Chapter
{
    public Chapter(
        string title,
        string chapterNumber,
        string sourceLanguage,
        string targetLanguage,
        IList<string> characters,
        IReadOnlyList<ChapterImage> images,
        IReadOnlyList<Bubble> bubbles)
    {
        Title = title;
        ChapterNumber = chapterNumber;
        SourceLanguage = sourceLanguage;
        TargetLanguage = targetLanguage;
        Characters = characters;
        Images = images;
        Bubbles = bubbles;
        LayoutStrip();
    }

    public string Title { get; }

    public string ChapterNumber { get; }

    public string SourceLanguage { get; }

    public string TargetLanguage { get; }

    /// <summary>Chapter character-name list (maintained automatically, never auto-removed).</summary>
    public IList<string> Characters { get; }

    /// <summary>Images in document order — which IS strip order, top to bottom.</summary>
    public IReadOnlyList<ChapterImage> Images { get; }

    /// <summary>Bubbles sorted by <see cref="Bubble.Order"/> across the whole chapter.</summary>
    public IReadOnlyList<Bubble> Bubbles { get; }

    /// <summary>Widest image; the strip is this wide and every image is left-aligned at x = 0.</summary>
    public int StripWidth { get; private set; }

    /// <summary>Total height of the strip — the sum of every image's height.</summary>
    public long StripHeight { get; private set; }

    /// <summary>
    /// Recomputes each image's <see cref="ChapterImage.StripTop"/> and the strip extents.
    /// Call after adding, removing or reordering images.
    /// </summary>
    public void LayoutStrip()
    {
        long top = 0;
        var width = 0;

        foreach (var image in Images)
        {
            image.StripTop = top;
            top += Math.Max(0, image.Height);
            width = Math.Max(width, image.Width);
        }

        StripHeight = top;
        StripWidth = width;
    }

    /// <summary>
    /// Maps a strip Y back to the image containing it and the offset inside that image.
    /// The two consumers that still need this are the tiled renderer and PSD export (§5.6).
    /// Out-of-range values clamp to the first/last image so callers never have to guard.
    /// </summary>
    public (int ImageIndex, int LocalY) Locate(long stripY)
    {
        if (Images.Count == 0)
        {
            return (-1, 0);
        }

        if (stripY < 0)
        {
            return (0, 0);
        }

        for (var i = 0; i < Images.Count; i++)
        {
            var image = Images[i];
            if (stripY < image.StripTop + image.Height)
            {
                return (i, (int)(stripY - image.StripTop));
            }
        }

        var last = Images[^1];
        return (Images.Count - 1, Math.Max(0, last.Height - 1));
    }
}
