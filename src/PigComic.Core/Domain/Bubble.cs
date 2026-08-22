using System.Xml.Linq;

namespace PigComic.Core.Domain;

/// <summary>
/// One translation unit: a source region + source text + target parts (+ metadata).
/// Mutable model object, optionally backed by its <c>&lt;bubble&gt;</c> XElement
/// (SPEC §5.8): when backed, property setters write through to the element immediately.
/// </summary>
public sealed class Bubble
{
    private readonly XElement? _backing;
    private string _rawOrder = "1";
    private string _rawKind;
    private string _rawStatus;
    private string _rawRegion = "";
    private string _rawSource = "";
    private string _rawNotes = "";
    private string _rawLlmComment = "";
    private string? _rawCharacter;
    private string _rawPageId;

    /// <summary>Element-less construction used by tests; parts validated to 1..3.</summary>
    public Bubble(
        string id,
        string pageId,
        int order,
        BubbleKind kind,
        BubbleStatus status = BubbleStatus.Untranslated,
        string? character = null,
        PixelRect? sourceRegion = null,
        string sourceText = "",
        IReadOnlyList<TargetPart>? parts = null,
        string notes = "",
        string llmComment = "")
    {
        if (parts is { Count: < 1 or > 3 })
        {
            throw new ArgumentOutOfRangeException(nameof(parts), "A bubble must have 1..3 parts.");
        }

        Id = id;
        _rawPageId = pageId;
        Order = order;
        _rawKind = kind.ToString();
        _rawStatus = status.ToString();
        Character = character;
        SourceRegion = sourceRegion ?? new PixelRect(0, 0, 1, 1);
        SourceText = sourceText;
        _parts.AddRange(parts is null
            ? [new TargetPart(1, SourceRegion, "")]
            : parts.ToList());
        Notes = notes;
        LlmComment = llmComment;
    }

    /// <summary>Backed construction: the given element is the live <c>&lt;bubble&gt;</c>.</summary>
    internal Bubble(XElement bubble)
    {
        _backing = bubble;
        Id = (string?)bubble.Attribute("id") ?? "";
        _rawPageId = (string?)bubble.Attribute("page") ?? "";
        _rawOrder = (string?)bubble.Attribute("order") ?? "1";
        _rawKind = (string?)bubble.Attribute("kind") ?? "";
        _rawStatus = (string?)bubble.Attribute("status") ?? "";
        _rawCharacter = (string?)bubble.Attribute("character");

        var regionEl = bubble.Element("region");
        _rawRegion = regionEl is null ? "" : TargetPart.RegionFromAttrs(regionEl);
        _rawSource = (string?)bubble.Element("source") ?? "";
        _rawNotes = (string?)bubble.Element("notes") ?? "";
        _rawLlmComment = (string?)bubble.Element("llmComment") ?? "";

        var targetEl = bubble.Element("target");
        if (targetEl is not null)
        {
            foreach (var partEl in targetEl.Elements("part"))
            {
                _parts.Add(new TargetPart(partEl));
            }
        }

        if (_parts.Count == 0)
        {
            _parts.Add(new TargetPart(1, SourceRegion, ""));
        }

        if (_parts.Count > 3)
        {
            _parts.RemoveRange(3, _parts.Count - 3);
        }
    }

    public string Id { get; }

    public string PageId
    {
        get => _rawPageId;
        set
        {
            _rawPageId = value;
            _backing?.SetAttributeValue("page", value);
        }
    }

    public int Order
    {
        get => int.TryParse(_rawOrder, out var v) ? v : 1;
        set
        {
            _rawOrder = value.ToString();
            _backing?.SetAttributeValue("order", value);
        }
    }

    public BubbleKind Kind
    {
        get => Enum.TryParse<BubbleKind>(_rawKind, out var v) ? v : BubbleKind.Speech;
        set
        {
            _rawKind = value.ToString();
            _backing?.SetAttributeValue("kind", _rawKind);
        }
    }

    internal string RawKind => _rawKind;

    public string? Character
    {
        get => _rawCharacter;
        set
        {
            _rawCharacter = value;
            if (value is null)
            {
                _backing?.Attribute("character")?.Remove();
            }
            else
            {
                _backing?.SetAttributeValue("character", value);
            }
        }
    }

    public BubbleStatus Status
    {
        get => Enum.TryParse<BubbleStatus>(_rawStatus, out var v) ? v : BubbleStatus.Untranslated;
        set
        {
            _rawStatus = value.ToString();
            _backing?.SetAttributeValue("status", _rawStatus);
        }
    }

    internal string RawStatus => _rawStatus;

    public PixelRect SourceRegion
    {
        get => ParseRegion(_rawRegion);
        set
        {
            _rawRegion = TargetPart.RegionString(value);
            _backing?.RegionElement().SetAttrs(value);
        }
    }

    internal string RawSourceRegion => _rawRegion;

    public string SourceText
    {
        get => _rawSource;
        set
        {
            value = TargetPart.NormalizeNewlines(value);
            _rawSource = value;
            var sourceEl = _backing?.Element("source");
            if (sourceEl is not null)
            {
                sourceEl.Value = value;
            }
            else if (_backing is not null)
            {
                _backing.Add(new XElement("source", value));
            }
        }
    }

    public IReadOnlyList<TargetPart> Parts => _parts;

    internal List<TargetPart> PartsList => _parts;

    private readonly List<TargetPart> _parts = [];

    public string Notes
    {
        get => _rawNotes;
        set
        {
            value = TargetPart.NormalizeNewlines(value);
            _rawNotes = value;
            var notesEl = _backing?.Element("notes");
            if (notesEl is not null)
            {
                notesEl.Value = value;
            }
            else if (_backing is not null && value.Length > 0)
            {
                _backing.Add(new XElement("notes", value));
            }
        }
    }

    public string LlmComment
    {
        get => _rawLlmComment;
        set
        {
            value = TargetPart.NormalizeNewlines(value);
            _rawLlmComment = value;
            var el = _backing?.Element("llmComment");
            if (el is not null)
            {
                el.Value = value;
            }
            else if (_backing is not null && value.Length > 0)
            {
                _backing.Add(new XElement("llmComment", value));
            }
        }
    }

    /// <summary>The TM unit target = parts' texts joined with "<c>\n</c>" (SPEC §5.3).</summary>
    public string TargetJoined => string.Join("\n", Parts.Select(p => p.Text));

    internal XElement? BackingElement => _backing;

    /// <summary>Removes the backing <c>&lt;bubble&gt;</c> element from the document.</summary>
    internal void RemoveFromDocument() => _backing?.Remove();

    public static PixelRect ParseRegion(string raw)
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