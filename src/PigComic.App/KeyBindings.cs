using Avalonia.Input;

namespace PigComic.App;

/// <summary>
/// M5.6 single source of truth for the §14.6 keyboard map. No scattered
/// literals; every binding-adjacent handler asks here. (Whole map: SPEC §14.6.)
/// </summary>
public static class KeyBindings
{
    public static bool IsConfirmNextUnconfirmed(KeyEventArgs e)
        => e.Key is Key.Enter or Key.Return &&
           e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
           !e.KeyModifiers.HasFlag(KeyModifiers.Shift);

    public static bool IsConfirmReviewed(KeyEventArgs e)
        => e.Key is Key.Enter or Key.Return &&
           e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
           e.KeyModifiers.HasFlag(KeyModifiers.Shift);

    public static bool IsCopySource(KeyEventArgs e)
        => e.Key == Key.Insert && e.KeyModifiers.HasFlag(KeyModifiers.Control);

    public static bool IsToggleLocked(KeyEventArgs e)
        => e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
           !e.KeyModifiers.HasFlag(KeyModifiers.Alt);

    public static bool IsSave(KeyEventArgs e)
        => e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
           !e.KeyModifiers.HasFlag(KeyModifiers.Shift);

    public static bool IsEditSource(KeyEventArgs e)
        => e.Key == Key.F2 && !e.KeyModifiers.HasFlag(KeyModifiers.Control);

    public static bool IsPreviousBubble(KeyEventArgs e)
        => e.Key == Key.Up && e.KeyModifiers.HasFlag(KeyModifiers.Control);

    public static bool IsNextBubble(KeyEventArgs e)
        => e.Key == Key.Down && e.KeyModifiers.HasFlag(KeyModifiers.Control);

    /// <summary>Ctrl+1..Ctrl+9 → result number (or null).</summary>
    public static int? NthMatch(KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return null;
        }

        if (e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            return e.Key - Key.D1 + 1;
        }

        if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9)
        {
            return e.Key - Key.NumPad1 + 1;
        }

        return null;
    }
}