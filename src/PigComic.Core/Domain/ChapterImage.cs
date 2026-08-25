using System.Xml.Linq;

namespace PigComic.Core.Domain;

/// <summary>
/// One image of the chapter strip: its media file name plus the ORIGINAL dimensions
/// (SPEC §5.6). Mutable model object, optionally backed by its <c>&lt;image&gt;</c>
/// XElement (SPEC §5.8): setters write through to the element immediately.
///
/// <para>This is NOT a page (D-49). Images are only the segments the continuous strip is
/// stored in; they carry no identity, nothing references them, and document order is strip
/// order. <see cref="StripTop"/> is assigned by <see cref="Chapter"/> when the strip is laid
/// out and is what turns a strip coordinate back into a file.</para>
/// </summary>
public sealed class ChapterImage
{
    private readonly XElement? _backing;
    private string _fileName = "";
    private int _width = 1;
    private int _height = 1;

    public ChapterImage(string fileName, int width, int height)
    {
        FileName = fileName;
        Width = width;
        Height = height;
    }

    internal ChapterImage(XElement image)
    {
        _backing = image;
        _fileName = (string?)image.Attribute("file") ?? "";
        _width = int.TryParse((string?)image.Attribute("width"), out var w) ? w : 1;
        _height = int.TryParse((string?)image.Attribute("height"), out var h) ? h : 1;
    }

    public string FileName
    {
        get => _fileName;
        set
        {
            _fileName = value;
            _backing?.SetAttributeValue("file", value);
        }
    }

    public int Width
    {
        get => _width;
        set
        {
            _width = value;
            _backing?.SetAttributeValue("width", value);
        }
    }

    public int Height
    {
        get => _height;
        set
        {
            _height = value;
            _backing?.SetAttributeValue("height", value);
        }
    }

    /// <summary>Strip Y of this image's top edge — the sum of the heights before it.
    /// Assigned by <see cref="Chapter.LayoutStrip"/>; not persisted (it is derived).</summary>
    public long StripTop { get; internal set; }
}
