using System.Xml.Linq;

namespace PigComic.Core.Domain;

/// <summary>
/// One of a bubble's 1..3 target sub-regions. Mutable model object, optionally
/// backed by its <c>&lt;part&gt;</c> XElement (SPEC §5.8): when backed, property
/// setters write through to the element immediately.
/// </summary>
public sealed class TargetPart
{
    private readonly XElement? _element;
    private string _rawRegion;
    private string _rawText = "";

    /// <summary>Element-less construction (used by domain tests and new bubbles without document backing).</summary>
    public TargetPart(int index, PixelRect region, string text)
    {
        if (index is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Part index must be 1..3.");
        }

        Index = index;
        _rawRegion = RegionString(region);
        _rawText = NormalizeNewlines(text);
    }

    /// <summary>Backed construction: the given element is the live <c>&lt;part&gt;</c>.</summary>
    internal TargetPart(XElement part)
    {
        _element = part;
        Index = int.TryParse((string?)part.Attribute("index"), out var idx) ? idx : 1;
        var regionEl = part.Element("region");
        _rawRegion = regionEl is null ? "" : RegionFromAttrs(regionEl);
        _rawText = NormalizeNewlines((string?)part.Element("text") ?? "");
    }

    public int Index { get; }

    public PixelRect Region
    {
        get => ParseRegion(_rawRegion);
        set
        {
            _rawRegion = RegionString(value);
            _element?.Element("region")?.SetAttrs(value);
        }
    }

    public string Text
    {
        get => _rawText;
        set
        {
            value = NormalizeNewlines(value);
            _rawText = value;
            var textEl = _element?.Element("text");
            if (textEl is not null)
            {
                textEl.Value = value;
            }
            else if (_element is not null)
            {
                _element.Add(new XElement("text", value));
            }
        }
    }

    internal static string RegionString(PixelRect r) => $"{r.X},{r.Y},{r.Width},{r.Height}";

    internal static string RegionFromAttrs(XElement regionEl)
    {
        string attr(XName name) => (string?)regionEl.Attribute(name) ?? "";
        return $"{attr("x")},{attr("y")},{attr("width")},{attr("height")}";
    }

    internal static string NormalizeNewlines(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');

    internal XElement? BackingElement => _element;

    private static PixelRect ParseRegion(string raw)
    {
        var parts = raw.Split(',');
        if (parts.Length == 4 &&
            int.TryParse(parts[0], out var x) &&
            int.TryParse(parts[1], out var y) &&
            int.TryParse(parts[2], out var w) &&
            int.TryParse(parts[3], out var h))
        {
            return new PixelRect(x, y, w, h);
        }

        return new PixelRect(0, 0, 1, 1);
    }
}

internal static class XElementExtensions
{
    internal static XElement RegionElement(this XElement part)
    {
        var region = part.Element("region");
        if (region is null)
        {
            region = new XElement("region");
            part.AddFirst(region);
        }

        return region;
    }

    internal static void SetAttrs(this XElement el, PixelRect r)
    {
        el.SetAttributeValue("x", r.X);
        el.SetAttributeValue("y", r.Y);
        el.SetAttributeValue("width", r.Width);
        el.SetAttributeValue("height", r.Height);
    }
}