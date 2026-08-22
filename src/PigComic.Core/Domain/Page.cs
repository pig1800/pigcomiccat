using System.Xml.Linq;

namespace PigComic.Core.Domain;

/// <summary>
/// One page: media file name inside <c>/media</c> + ORIGINAL image dimensions
/// (SPEC §5.6). Mutable model object, optionally backed by its <c>&lt;page&gt;</c>
/// XElement (SPEC §5.8): setters write through to the element immediately.
/// </summary>
public sealed class Page
{
    private readonly XElement? _backing;
    private string _rawFileName = "";
    private string _rawWidth = "1";
    private string _rawHeight = "1";

    public Page(string id, string fileName, int width, int height)
    {
        Id = id;
        FileName = fileName;
        Width = width;
        Height = height;
    }

    internal Page(XElement page)
    {
        _backing = page;
        Id = (string?)page.Attribute("id") ?? "";
        FileName = (string?)page.Attribute("file") ?? "";
        Width = int.TryParse((string?)page.Attribute("width"), out var w) ? w : 1;
        Height = int.TryParse((string?)page.Attribute("height"), out var h) ? h : 1;
    }

    public string Id { get; }

    public string FileName
    {
        get => _rawFileName;
        set
        {
            _rawFileName = value;
            _backing?.SetAttributeValue("file", value);
        }
    }

    public int Width
    {
        get => int.TryParse(_rawWidth, out var v) ? v : 1;
        set
        {
            _rawWidth = value.ToString();
            _backing?.SetAttributeValue("width", value);
        }
    }

    public int Height
    {
        get => int.TryParse(_rawHeight, out var v) ? v : 1;
        set
        {
            _rawHeight = value.ToString();
            _backing?.SetAttributeValue("height", value);
        }
    }
}