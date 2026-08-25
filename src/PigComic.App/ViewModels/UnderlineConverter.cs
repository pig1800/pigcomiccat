using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;

namespace PigComic.App.ViewModels;

/// <summary>bool → text-decoration collection for the source-diff underline rows (SPEC §9).</summary>
public static class UnderlineConverter
{
    public static readonly IValueConverter Instance =
        new FuncValueConverter<bool, TextDecorationCollection?>(
            b => b ? TextDecorations.Underline : null);
}