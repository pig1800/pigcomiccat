using Avalonia;

namespace PigComic.App.Controls;

/// <summary>
/// M6.1/M6.2 marker interaction state machine (SPEC §15). Drag-to-move of a bubble's
/// source marker or one of its part markers, Ctrl+B placement mode, Esc cancellation.
///
/// <para>Pure state + hit-testing in STRIP coordinates; <c>TiledImageControl</c> feeds it
/// pointer positions and raises the commit/place events to the editor, which applies the
/// Core mutation (<c>SetMarker</c>/<c>SetPartMarker</c>/<c>AddBubble</c>) and marks dirty.
/// The move commits on mouse-up as one step (SPEC §15.1); during the drag the control
/// renders the preview position locally — the model is never touched mid-drag.</para>
/// </summary>
public sealed class MarkerInteraction
{
    /// <summary>What a press at a strip point grabs. PartIndex is 0-based into the
    /// marker's part-points list (1-based part index = +1).</summary>
    public enum GrabKind
    {
        None,
        Source,
        Part,
    }

    public sealed record Grab(GrabKind Kind, string BubbleId, int PartIndex)
    {
        public static readonly Grab Empty = new(GrabKind.None, "", -1);
    }

    /// <summary>An in-flight drag: which marker, where it started, and the grab offset.</summary>
    public sealed record DragState(
        string BubbleId,
        int? PartIndex,
        Point OriginalStrip,
        Point GrabOffsetStrip);

    public bool PlacementArmed { get; set; }

    public DragState? Drag { get; set; }

    public void Cancel()
    {
        Drag = null;
        PlacementArmed = false;
    }

    /// <summary>True when a press consumed the event (placed a marker or grabbed one).</summary>
    public bool AnyActive => PlacementArmed || Drag is not null;
}
