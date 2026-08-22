using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PigComic.App.Views;

/// <summary>Simple modal message dialog used across the app until M8's richer dialogs.</summary>
public partial class ContentDialog : Avalonia.Controls.Window
{
    private bool _result;

    public ContentDialog(string title, string message, string? okText, string? cancelText)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        OkButton.Content = okText ?? "OK";
        CancelButton.Content = cancelText ?? "Cancel";
        CancelButton.IsVisible = cancelText is not null;
    }

    /// <summary>Shows the dialog; returns true when OK was clicked (false = cancel/close).</summary>
    public static bool? Ask(Avalonia.Controls.Window owner, string title, string message, string? okText, string? cancelText)
    {
        var dlg = new ContentDialog(title, message, okText, cancelText);
        return dlg.ShowDialog<bool?>(owner).GetAwaiter().GetResult() ?? false;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        _result = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}