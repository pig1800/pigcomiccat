using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using PigComic.App.Services;
using PigComic.App.ViewModels;

namespace PigComic.App.Views;

/// <summary>
/// M7.1 master character editor (SPEC §16): a grid of §6.3 rows over characters.json.
/// Reachable from the project view and from the character box's "add to master" offer
/// (prefilled with the new name).
/// </summary>
public partial class CharacterMasterWindow : Window
{
    private readonly CharacterMasterViewModel _vm;

    public CharacterMasterWindow(string projectFolder, string? prefillName = null)
    {
        InitializeComponent();
        _vm = new CharacterMasterViewModel(projectFolder, prefillName);
        DataContext = _vm;
        RowList.SelectedIndex = _vm.Rows.Count > 0 ? 0 : -1;
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+V pastes a clipboard image into the SELECTED row (SPEC §16: image cell paste).
        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && RowList.SelectedItem is CharacterMasterRowViewModel row)
        {
            e.Handled = true;
            _ = PasteClipboardAsync(row);
        }
    }

    private async Task PasteClipboardAsync(CharacterMasterRowViewModel row)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            var bitmap = clipboard is null ? null : await clipboard.TryGetBitmapAsync();
            if (bitmap is Bitmap bmp)
            {
                _vm.ApplyBitmap(row, bmp);
            }
        }
        catch
        {
            // clipboard unavailable — ignore
        }
    }

    private void OnPasteClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is CharacterMasterRowViewModel row)
        {
            _ = PasteClipboardAsync(row);
        }
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not CharacterMasterRowViewModel row)
        {
            return;
        }

        var picked = await FilePickers.OpenFileAsync(this, "Character image", "Image", "png").ConfigureAwait(true);
        if (picked is not null)
        {
            _vm.ApplyImage(row, picked);
        }
    }

    private void OnAddClick(object? sender, RoutedEventArgs e)
    {
        _vm.AddRow();
        RowList.SelectedIndex = _vm.Rows.Count - 1;
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not CharacterMasterRowViewModel row)
        {
            return;
        }

        var name = row.Name.Length > 0 ? row.Name : "(unnamed)";
        if (await ContentDialog.AskAsync(this, "Delete character",
                $"Delete '{name}' from the master list?", "Delete", "Cancel"))
        {
            _vm.DeleteRow(row);
        }
    }

    private async void OnDoneClick(object? sender, RoutedEventArgs e)
    {
        if (_vm.Save())
        {
            Close();
        }
    }
}
