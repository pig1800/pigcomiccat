using System.Xml.Linq;

namespace PigComic.Core.Domain;

/// <summary>
/// One of a bubble's 1..3 target pieces, each anchored by its own marker. Mutable model
/// object, optionally backed by its <c>&lt;part&gt;</c> XElement (SPEC §5.8): when backed,
/// property setters write through to the element immediately.
/// </summary>
public sealed class TargetPart
{
    private readonly XElement? _element;
    private string _rawMarker;
    private string _rawText = "";

    /// <summary>Element-less construction (used by domain tests and new bubbles without document backing).</summary>
    public TargetPart(int index, PixelPoint marker, string text)
    {
        if (index is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Part index must be 1..3.");
        }

        Index = index;
        _rawMarker = MarkerString(marker);
        _rawText = NormalizeNewlines(text);
    }

    /// <summary>Backed construction: the given element is the live <c>&lt;part&gt;</c>.</summary>
    internal TargetPart(XElement part)
    {
        _element = part;
        Index = int.TryParse((string?)part.Attribute("index"), out var idx) ? idx : 1;
        var markerEl = part.Element("marker");
        _rawMarker = markerEl is null ? "" : MarkerFromAttrs(markerEl);
        _rawText = NormalizeNewlines((string?)part.Element("text") ?? "");
    }

    public int Index { get; }

    /// <summary>Where this part's text starts, in strip coordinates (D-50).</summary>
    public PixelPoint Marker
    {
        get => ParseMarker(_rawMarker);
        set
        {
            _rawMarker = MarkerString(value);
            _element?.MarkerElement().SetAttrs(value);
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

    internal static string MarkerString(PixelPoint p) => $"{p.X},{p.Y}";

    internal static string MarkerFromAttrs(XElement markerEl)
    {
        string attr(XName name) => (string?)markerEl.Attribute(name) ?? "";
        return $"{attr("x")},{attr("y")}";
    }

    internal static string NormalizeNewlines(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');

    internal XElement? BackingElement => _element;

    private static PixelPoint ParseMarker(string raw)
    {
        var parts = raw.Split(',');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var x) &&
            int.TryParse(parts[1], out var y))
        {
            return new PixelPoint(x, y);
        }

        return PixelPoint.Origin;
    }
}

internal static class XElementExtensions
{
    /// <summary>The element's <c>&lt;marker&gt;</c> child, created first-in-order if absent.</summary>
    internal static XElement MarkerElement(this XElement owner)
    {
        var marker = owner.Element("marker");
        if (marker is null)
        {
            marker = new XElement("marker");
            owner.AddFirst(marker);
        }

        return marker;
    }

    internal static void SetAttrs(this XElement el, PixelPoint p)
    {
        el.SetAttributeValue("x", p.X);
        el.SetAttributeValue("y", p.Y);
    }
}
