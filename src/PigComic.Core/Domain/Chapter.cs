namespace PigComic.Core.Domain;

/// <summary>
/// In-memory view of one .pcml chapter (SPEC §4). Built by the package reader;
/// the XDocument remains the persistence model (SPEC §5.8).
/// </summary>
public sealed class Chapter
{
    public Chapter(
        string title,
        string chapterNumber,
        string sourceLanguage,
        string targetLanguage,
        IList<string> characters,
        IReadOnlyList<Page> pages,
        IReadOnlyList<Bubble> bubbles)
    {
        Title = title;
        ChapterNumber = chapterNumber;
        SourceLanguage = sourceLanguage;
        TargetLanguage = targetLanguage;
        Characters = characters;
        Pages = pages;
        Bubbles = bubbles;
    }

    public string Title { get; }

    public string ChapterNumber { get; }

    public string SourceLanguage { get; }

    public string TargetLanguage { get; }

    /// <summary>Chapter character-name list (maintained automatically, never auto-removed).</summary>
    public IList<string> Characters { get; }

    /// <summary>Pages in document (reading) order.</summary>
    public IReadOnlyList<Page> Pages { get; }

    /// <summary>Bubbles sorted by (page order, Order).</summary>
    public IReadOnlyList<Bubble> Bubbles { get; }
}