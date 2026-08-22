using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PigComic.App.Views;

/// <summary>One missing chapter row in the relink dialog (SPEC §6.6).</summary>
public sealed class RelinkRow
{
    public required string MissingPath { get; set; }
    public required string FileName { get; set; }
    public string? ResolvedPath { get; set; }

    public bool IsResolved => ResolvedPath is not null;
}

/// <summary>
/// SPEC §6.6 relink: per-file Browse, Search-folder bulk resolve by exact file
/// name, OK enabled only when all are resolved. Cancel → stay on the project
/// list (caller closes its window and does not enter the project).
/// </summary>
public partial class RelinkDialog : Avalonia.Controls.Window
{
    private readonly List<RelinkRow> _rows = [];

    public bool Cancelled { get; private set; } = true;

    /// <summary>Resolved paths in the same order as the input (valid when !Cancelled).</summary>
    public IReadOnlyList<string> ResolvedPaths => _rows.Select(r => r.ResolvedPath!).ToList();

    public RelinkDialog(IReadOnlyList<string> missingPaths)
    {
        InitializeComponent();
        foreach (var path in missingPaths)
        {
            _rows.Add(new RelinkRow { MissingPath = path, FileName = Path.GetFileName(path) });
        }

        Rebuild();
    }

    private void Rebuild()
    {
        RowsPanel.Children.Clear();
        foreach (var row in _rows)
        {
            var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
            var text = new TextBlock
            {
                Text = row.IsResolved ? $"{row.FileName}  →  {row.ResolvedPath}" : $"{row.FileName}  ⚠ missing ({row.MissingPath})",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            grid.Children.Add(text);
            var browse = new Button { Content = row.IsResolved ? "Change…" : "Browse…", Margin = new Avalonia.Thickness(8, 0, 0, 0) };
            var textClosure = text;
            browse.Click += (_, _) => _ = BrowseRow(row, textClosure);
            Grid.SetColumn(browse, 1);
            grid.Children.Add(browse);
            RowsPanel.Children.Add(grid);
        }

        UpdateOk();
    }

    private async Task BrowseRow(RelinkRow row, TextBlock text)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Locate {row.FileName}",
            AllowMultiple = false,
            Filters = { new FileDialogFilter { Name = "PigComic chapter", Extensions = { "pcml" } } },
        };
        if (dialog.ShowAsync(this).GetAwaiter().GetResult() is { Length: > 0 } picked)
        {
            row.ResolvedPath = picked[0];
            text.Text = $"{row.FileName}  →  {row.ResolvedPath}";
            UpdateOk();
        }
    }

    private void UpdateOk() => OkButton.IsEnabled = _rows.All(r => r.IsResolved);

    private void OnSearchFolderClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Search folder for missing files" };
        if (dialog.ShowAsync(this).GetAwaiter().GetResult() is not { } folder)
        {
            return;
        }

        var files = Directory.EnumerateFiles(folder, "*.pcml", SearchOption.TopDirectoryOnly)
            .ToDictionary(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows.Where(r => !r.IsResolved))
        {
            if (files.TryGetValue(row.FileName, out var hit))
            {
                row.ResolvedPath = hit;
            }
        }

        Rebuild();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Cancelled = true;
        Close(false);
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (_rows.All(r => r.IsResolved))
        {
            Cancelled = false;
            Close(true);
        }
    }
}