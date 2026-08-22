using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PigComic.App.Views;

/// <summary>Simple modal message dialog used across the app until M8's richer dialogs.</summary>
public partial class ContentDialog : Avalonia.Controls.Window
{
    public ContentDialog(string title, string message, string? okText, string? cancelText)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        OkButton.Content = okText ?? "OK";
        CancelButton.Content = cancelText ?? "Cancel";
        CancelButton.IsVisible = cancelText is not null;
    }

    /// <summary>Shows the dialog; true when OK, false otherwise.</summary>
    public static Task<bool?> ShowAsync(Avalonia.Controls.Window owner, string title, string message, string? okText, string? cancelText)
    {
        var dlg = new ContentDialog(title, message, okText, cancelText);
        return dlg.ShowDialog<bool?>(owner);
    }

    /// <summary>Async convenience: true when the user confirmed.</summary>
    public static async Task<bool> AskAsync(Avalonia.Controls.Window owner, string title, string message, string? okText, string? cancelText)
        => await ShowAsync(owner, title, message, okText, cancelText).ConfigureAwait(true) == true;

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}