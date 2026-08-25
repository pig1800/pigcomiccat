using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using PigComic.Core.Domain;

namespace PigComic.Core.Package;

/// <summary>
/// Typed mutations used by the UI and the undo system (SPEC §15.2/§15.3, D-17,
/// D-18). Each returns a <see cref="MutationRecord"/> capturing the operation and
/// the "before" state needed to revert it (reused by the M9 undo stack).
/// </summary>
public static class BubbleMutations
{
    /// <summary>
    /// Vertical gap between the default markers of a split target's parts, in strip pixels
    /// (SPEC §15.3 / D-18). Just far enough that two crosses never overlap; the user drags
    /// them where they actually belong.
    /// </summary>
    public const int PartMarkerStep = 48;

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string J<T>(T p) => JsonSerializer.Serialize(p, Json);

    private static TargetPart PartOrThrow(Bubble bubble, int index)
    {
        if (index < 1 || index > bubble.Parts.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return bubble.Parts[index - 1];
    }

    private static string MarkerToString(PixelPoint p) => $"{p.X},{p.Y}";

    private static XElement MarkerElement(PixelPoint p) =>
        new("marker",
            new XAttribute("x", p.X),
            new XAttribute("y", p.Y));

    /// Default markers for a split target (D-18, amended by D-50): part 1 sits on the
    /// source marker and each later part is offset <see cref="PartMarkerStep"/> px further
    /// down the strip, so the crosses never land on top of one another.
    /// </summary>
    private static IReadOnlyList<PixelPoint> StackedMarkers(PixelPoint source, int count)
    {
        var markers = new PixelPoint[count];
        for (var i = 0; i < count; i++)
        {
            markers[i] = new PixelPoint(source.X, source.Y + (i * PartMarkerStep));
        }

        return markers;
    }

    private static XElement TargetElement(Bubble bubble) =>
        bubble.BackingElement?.Element("target")
        ?? throw new InvalidOperationException($"Bubble '{bubble.Id}' has no <target> element.");

    // ---------------------------------------------------------------- setters

    public static MutationRecord SetSource(Bubble bubble, string text)
    {
        var before = bubble.SourceText;
        bubble.SourceText = text;
        return new MutationRecord("SetSource", J(new { id = bubble.Id, index = 0, before }));
    }

    public static MutationRecord SetPartText(Bubble bubble, int partIndex, string text)
    {
        var part = PartOrThrow(bubble, partIndex);
        var before = part.Text;
        part.Text = text;
        return new MutationRecord("SetPartText", J(new { id = bubble.Id, index = partIndex, before }));
    }

    /// <summary>
    /// Set target part count 1..3 (SPEC §15.3). Reducing to 1 joins texts with "\n"
    /// into part 1 and resets its marker to the source marker; reducing 3→2 drops
    /// the last part. Increasing keeps existing text in part 1 and gives new parts
    /// empty texts with stacked default markers (D-18).
    /// </summary>
    public static MutationRecord SetPartCount(Bubble bubble, int count)
    {
        if (count is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Part count must be 1..3.");
        }

        var before = bubble.Parts.Count;
        if (count == before)
        {
            return new MutationRecord("SetPartCount", J(new { id = bubble.Id, before, after = count }));
        }

        if (count < before)
        {
            // Merge: join all texts into part 1, reset its marker to the source marker.
            var joined = string.Join("\n", bubble.Parts.Select(p => p.Text));
            bubble.Parts[0].Text = joined;
            bubble.Parts[0].Marker = bubble.Marker;
            while (bubble.Parts.Count > count)
            {
                var index = bubble.Parts.Count;
                bubble.PartsList[index - 1].Text = ""; // not needed; remove below
                RemovePartAt(bubble, index);
            }
        }
        else
        {
            var markers = StackedMarkers(bubble.Marker, count);
            var targetEl = TargetElement(bubble);
            for (var i = bubble.Parts.Count; i < count; i++)
            {
                var partEl = new XElement("part", new XAttribute("index", i + 1),
                    MarkerElement(markers[i]), new XElement("text", ""));
                targetEl.Add(partEl);
                bubble.PartsList.Add(new TargetPart(partEl));
            }
        }

        return new MutationRecord("SetPartCount", J(new { id = bubble.Id, before, after = count }));
    }

    private static void RemovePartAt(Bubble bubble, int index)
    {
        var part = bubble.Parts[index - 1];
        part.BackingElement?.Remove();
        bubble.PartsList.RemoveAt(index - 1);
    }

    public static MutationRecord SetStatus(Bubble bubble, BubbleStatus status)
    {
        var before = bubble.Status;
        bubble.Status = status;
        return new MutationRecord("SetStatus", J(new { id = bubble.Id, before = before.ToString() }));
    }

    public static MutationRecord SetKind(Bubble bubble, BubbleKind kind)
    {
        var before = bubble.Kind;
        bubble.Kind = kind;
        return new MutationRecord("SetKind", J(new { id = bubble.Id, before = before.ToString() }));
    }

    /// <summary>Sets the speaker; the name is auto-added to the chapter character list.</summary>
    public static MutationRecord SetCharacter(PcmlDocument doc, Bubble bubble, string? character)
    {
        var before = bubble.Character;
        bubble.Character = character;
        if (!string.IsNullOrEmpty(character) && !doc.Model.Characters.Contains(character))
        {
            doc.Model.Characters.Add(character);
            doc.ContentXml.Root?.Element("characters")?.Add(
                new XElement("character", new XAttribute("name", character)));
        }

        return new MutationRecord("SetCharacter", J(new { id = bubble.Id, before }));
    }

    public static MutationRecord SetNotes(Bubble bubble, string notes)
    {
        var before = bubble.Notes;
        bubble.Notes = notes;
        return new MutationRecord("SetNotes", J(new { id = bubble.Id, before }));
    }

    public static MutationRecord SetLlmComment(Bubble bubble, string comment)
    {
        var before = bubble.LlmComment;
        bubble.LlmComment = comment;
        return new MutationRecord("SetLlmComment", J(new { id = bubble.Id, before }));
    }

    public static MutationRecord SetMarker(Bubble bubble, PixelPoint marker)
    {
        var before = bubble.Marker;
        bubble.Marker = marker;
        return new MutationRecord("SetMarker", J(new { id = bubble.Id, before = MarkerToString(before) }));
    }

    public static MutationRecord SetPartMarker(Bubble bubble, int partIndex, PixelPoint marker)
    {
        var part = PartOrThrow(bubble, partIndex);
        var before = part.Marker;
        part.Marker = marker;
        return new MutationRecord("SetPartMarker", J(new { id = bubble.Id, index = partIndex, before = MarkerToString(before) }));
    }

    public static MutationRecord SetOrder(Bubble bubble, int order)
    {
        var before = bubble.Order;
        bubble.Order = order;
        return new MutationRecord("SetOrder", J(new { id = bubble.Id, before }));
    }

    // ---------------------------------------------------------------- create/delete

    /// <summary>
    /// Creates a new bubble (SPEC §15.2 / D-03, D-17): id "u" + 8 lowercase hex,
    /// collision-checked; kind Speech, status Untranslated, empty source/target,
    /// one part whose marker equals the source marker. Reading order is chapter-global
    /// (D-49): after the last bubble whose marker Y is at or above the new one (ties after);
    /// subsequent orders renumbered. Refreshes the document model.
    /// </summary>
    public static MutationRecord AddBubble(PcmlDocument doc, PixelPoint marker, out Bubble created)
    {
        var root = doc.ContentXml.Root ?? throw new InvalidOperationException("No document root.");
        var bubblesEl = root.Element("bubbles") ?? throw new InvalidOperationException("No <bubbles> element.");
        var id = NewBubbleId(doc);

        var bubbleEl = new XElement("bubble",
            new XAttribute("id", id),
            new XAttribute("order", 1),
            new XAttribute("kind", "Speech"),
            new XAttribute("status", "Untranslated"),
            MarkerElement(marker));
        bubbleEl.Add(new XElement("source", ""));
        var target = new XElement("target");
        target.Add(new XElement("part", new XAttribute("index", "1"),
            MarkerElement(marker), new XElement("text", "")));
        bubbleEl.Add(target);
        bubblesEl.Add(bubbleEl);

        // Order insertion (Y-only, D-36): after the last bubble whose top-Y <= the
        // new top-Y (ties after per D-17); subsequent orders renumbered.
        var existing = doc.Model.Bubbles.OrderBy(b => b.Order).ToList();
        var newOrder = existing.Count(b => b.Marker.Y <= marker.Y) + 1;
        bubbleEl.SetAttributeValue("order", newOrder);
        foreach (var b in existing.Where(b => b.Order >= newOrder).OrderByDescending(b => b.Order))
        {
            b.Order = b.Order + 1;
        }

        doc.RefreshModel();
        created = doc.Model.Bubbles.First(b => b.Id == id);
        return new MutationRecord("AddBubble", J(new { id, order = newOrder }));
    }

    /// <summary>Removes the bubble element and refreshes the model; record carries the full restore info.</summary>
    public static MutationRecord DeleteBubble(PcmlDocument doc, Bubble bubble)
    {
        var snapshot = new
        {
            id = bubble.Id,
            order = bubble.Order,
            kind = bubble.RawKind,
            status = bubble.RawStatus,
            character = bubble.Character,
            marker = MarkerToString(bubble.Marker),
            source = bubble.SourceText,
            parts = bubble.Parts.Select(p => new { p.Index, marker = MarkerToString(p.Marker), p.Text }).ToArray(),
            notes = bubble.Notes,
            llmComment = bubble.LlmComment,
        };
        bubble.RemoveFromDocument();
        doc.RefreshModel();
        return new MutationRecord("DeleteBubble", JsonSerializer.Serialize(snapshot, Json));
    }

    // ---------------------------------------------------------------- internals

    private static string NewBubbleId(PcmlDocument doc)
    {
        var existing = new HashSet<string>(doc.Model.Bubbles.Select(b => b.Id), StringComparer.Ordinal);
        while (true)
        {
            var id = "u" + Convert.ToHexString(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            if (existing.Add(id))
            {
                return id;
            }
        }
    }
}