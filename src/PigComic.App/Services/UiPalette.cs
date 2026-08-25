using Avalonia.Media;
using PigComic.Core.Domain;

namespace PigComic.App.Services;

/// <summary>
/// Shared status colors (SPEC §14.2/§14.3): Untranslated gray, Draft amber,
/// Translated green, Reviewed blue, Locked purple. Used by the image-pane
/// overlay outlines, the segment-row tint and the target editors.
/// </summary>
public static class UiPalette
{
    public static Color StatusColor(BubbleStatus status) => status switch
    {
        BubbleStatus.Untranslated => Color.FromRgb(0x9E, 0x9E, 0x9E),
        BubbleStatus.Draft => Color.FromRgb(0xF0, 0xA0, 0x20),
        BubbleStatus.Translated => Color.FromRgb(0x2E, 0x9E, 0x44),
        BubbleStatus.Reviewed => Color.FromRgb(0x2B, 0x7E, 0xC6),
        BubbleStatus.Locked => Color.FromRgb(0x8E, 0x5B, 0xC6),
        _ => Colors.Gray,
    };

    /// <summary>Row background tint at low opacity (SPEC §14.3).</summary>
    public static IBrush StatusTint(BubbleStatus status)
    {
        var c = StatusColor(status);
        return new SolidColorBrush(new Color(c.A, c.R, c.G, c.B));
    }
}