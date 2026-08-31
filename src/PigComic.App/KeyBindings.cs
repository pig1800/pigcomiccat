using Avalonia.Input;

namespace PigComic.App;

/// <summary>
/// M5.6 single source of truth for the §14.6 keyboard map. No scattered
/// literals; every binding-adjacent handler asks here. (Whole map: SPEC §14.6.)
/// </summary>
public static class KeyBindings
{
    /// <summary>Ctrl+Enter: confirm as Translated and advance (D-52 — plain Enter is a newline in part editors).</summary>
    public static bool IsConfirm(KeyEventArgs e)
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

    /// <summary>Ctrl+B: arm place-new-bubble mode (SPEC §15.2).</summary>
    public static bool IsPlaceMarker(KeyEventArgs e)
        => e.Key == Key.B && e.KeyModifiers == KeyModifiers.Control;

    /// <summary>Delete (no modifiers): delete the selected bubble (SPEC §14.6).</summary>
    public static bool IsDeleteBubble(KeyEventArgs e)
        => e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None;

    /// <summary>Alt+1/2/3 → target part count (SPEC §15.3), or null.</summary>
    public static int? SetPartCount(KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt) ||
            e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return null;
        }

        return e.Key switch
        {
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            _ => null,
        };
    }

    /// <summary>Tab: next part (SPEC §14.3/§15.3).</summary>
    public static bool IsNextPart(KeyEventArgs e)
        => e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None;

    /// <summary>Shift+Tab: previous part.</summary>
    public static bool IsPrevPart(KeyEventArgs e)
        => e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Shift;

    /// <summary>Ctrl+Shift+K: focus the kind selector (SPEC §14.6).</summary>
    public static bool IsFocusKind(KeyEventArgs e)
        => e.Key == Key.K && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift);

    /// <summary>Ctrl+Shift+C: focus the character box.</summary>
    public static bool IsFocusCharacter(KeyEventArgs e)
        => e.Key == Key.C && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift);

    /// <summary>Ctrl+Shift+N: focus the notes field.</summary>
    public static bool IsFocusNotes(KeyEventArgs e)
        => e.Key == Key.N && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift);

    /// <summary>Ctrl+Shift+M: open the master character editor (SPEC §14.6, D-58 direct entrance).</summary>
    public static bool IsOpenMaster(KeyEventArgs e)
        => e.Key == Key.M && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift);

    /// <summary>F8: run mechanical QA on the chapter (SPEC §12/§14.6).</summary>
    public static bool IsRunQa(KeyEventArgs e)
        => e.Key == Key.F8;

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