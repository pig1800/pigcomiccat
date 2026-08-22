using Avalonia.Controls;
using Avalonia.Interactivity;
using PigComic.Core.Adapters;
using PigComic.Core.Export;
using PigComic.Core.Exchange;
using PigComic.Core.Package;
using PigComic.Core.Project;

namespace PigComic.App.Views;

public sealed class ChapterRow
{
    public required string Path { get; set; }
    public required string FileName { get; set; }
    public string ChapterNumber { get; set; } = "";
    public string MetaTitle { get; set; } = "";
    public string ExistsText { get; set; } = "yes";
    public Avalonia.Media.IBrush ExistsBrush
        => ExistsText == "yes" ? Avalonia.Media.Brushes.DarkGreen
           : ExistsText == "unreadable" ? Avalonia.Media.Brushes.DarkOrange
           : Avalonia.Media.Brushes.IndianRed;

    /// <summary>Refreshes meta fields by peeking the .pcml header when possible.</summary>
    public void Peek()
    {
        if (!File.Exists(Path))
        {
            ExistsText = "⚠ missing";
            return;
        }

        try
        {
            using var doc = PcmlDocument.Open(Path);
            ChapterNumber = doc.Model.ChapterNumber;
            MetaTitle = doc.Model.Title;
            ExistsText = "yes";
        }
        catch (Exception)
        {
            ChapterNumber = "?";
            MetaTitle = "?";
            ExistsText = "unreadable";
        }
    }
}

public partial class ProjectView : Avalonia.Controls.Window
{
    private ProjectFile? _project;
    private readonly string _folder;
    private readonly List<ChapterRow> _rows = [];

    public ProjectView(string projectJsonPath)
    {
        InitializeComponent();
        _project = ProjectFile.Load(projectJsonPath);
        _folder = Path.GetDirectoryName(projectJsonPath) ?? "";
        ChapterList.SelectionChanged += (_, _) => UpdateButtons();
        ExchangeButton.Click += OnExchangeClick;
        Reload();
    }

    private void Reload()
    {
        if (_project is null)
        {
            return;
        }

        Title = $"Project — {_project.Title}";
        _rows.Clear();
        foreach (var chapter in _project.ChapterPaths)
        {
            var row = new ChapterRow { Path = chapter, FileName = Path.GetFileName(chapter) };
            row.Peek();
            _rows.Add(row);
        }

        ChapterList.ItemsSource = _rows;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var has = ChapterList.SelectedItem is not null;
        OpenChapterButton.IsEnabled = has;
        RemoveChapterButton.IsEnabled = has;
        MoveUpButton.IsEnabled = has;
        MoveDownButton.IsEnabled = has;
        StatsButton.IsEnabled = _rows.Count > 0;
        CharactersButton.IsEnabled = true;
    }

    private void OnAddChapterClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add .pcml chapter",
            AllowMultiple = false,
            Filters = { new FileDialogFilter { Name = "PigComic chapter", Extensions = { "pcml" } } },
        };
        if (dialog.ShowAsync(this).GetAwaiter().GetResult() is { Length: > 0 } picked)
        {
            _project?.AddChapter(picked[0]);
            SaveProject();
        }
    }

    private void OnRemoveChapterClick(object? sender, RoutedEventArgs e)
    {
        if (ChapterList.SelectedItem is not ChapterRow row)
        {
            return;
        }

        if (ContentDialog.Ask(this, "Remove chapter from project?",
                "The .pcml file itself is never deleted.", "Remove", "Cancel") == true)
        {
            _project?.RemoveChapter(row.Path);
            SaveProject();
        }
    }

    private void OnOpenChapterClick(object? sender, RoutedEventArgs e)
    {
        if (ChapterList.SelectedItem is not ChapterRow row)
        {
            return;
        }

        ContentDialog.Ask(this, "Open chapter",
            row.ExistsText == "yes"
                ? $"Would open {row.FileName} — the editor ships in M5."
                : $"File missing: {row.Path}\nUse the Relink flow when it lands (M4.7).",
            "OK", null);
    }

    private void OnMoveUpClick(object? sender, RoutedEventArgs e)
    {
        var index = ChapterList.SelectedIndex;
        if (index <= 0 || _project is null)
        {
            return;
        }

        var paths = _project.ChapterPaths.ToList();
        (paths[index - 1], paths[index]) = (paths[index], paths[index - 1]);
        _project.ReorderChapters(paths);
        ChapterList.SelectedIndex = index - 1;
        SaveProject();
    }

    private void OnMoveDownClick(object? sender, RoutedEventArgs e)
    {
        var index = ChapterList.SelectedIndex;
        if (index < 0 || index >= _rows.Count - 1 || _project is null)
        {
            return;
        }

        var paths = _project.ChapterPaths.ToList();
        (paths[index + 1], paths[index]) = (paths[index], paths[index + 1]);
        _project.ReorderChapters(paths);
        ChapterList.SelectedIndex = index + 1;
        SaveProject();
    }

    private async void OnExchangeClick(object? sender, RoutedEventArgs e)
    {
        var menu = new Menu();
        var items = new (string Header, Func<Task> Run)[]
        {
            ("Import TM… (TMX)", () => ImportAsync("TM (TMX)", ".tmx", true, false)),
            ("Export TM… (TMX)", () => ExportAsync("TM (TMX)", ".tmx", true, false)),
            ("Import TM… (XLSX)", () => ImportAsync("TM (XLSX)", ".xlsx", true, false)),
            ("Export TM… (XLSX)", () => ExportAsync("TM (XLSX)", ".xlsx", true, false)),
            ("Import TB… (TBX)", () => ImportAsync("TB (TBX)", ".tbx", false, true)),
            ("Export TB… (TBX)", () => ExportAsync("TB (TBX)", ".tbx", false, true)),
            ("Import TB… (XLSX)", () => ImportAsync("TB (XLSX)", ".xlsx", false, true)),
            ("Export TB… (XLSX)", () => ExportAsync("TB (XLSX)", ".xlsx", false, true)),
        };
        foreach (var (header, run) in items)
        {
            var item = new MenuItem { Header = header, Tag = run };
            item.Click += async (_, _) => await ((Func<Task>)item.Tag!)().ConfigureAwait(true);
            menu.Items.Add(item);
        }

        menu.Open();
    }

    private async Task ImportAsync(string caption, string ext, bool isTm, bool isTb)
    {
        var dialog = new OpenFileDialog
        {
            Title = $"Import {caption}",
            AllowMultiple = false,
            Filters = { new FileDialogFilter { Name = caption, Extensions = { ext.TrimStart('.') } } },
        };
        if (dialog.ShowAsync(this).GetAwaiter().GetResult() is not { } picked || _project is null || picked.Length == 0)
        {
            return;
        }

        var importPath = picked[0];
        var opened = ProjectFolder.OpenStores(_folder, _project.SourceLanguage, _project.TargetLanguage);
        using (opened.Tm)
        using (opened.Tb)
        {
            ImportReport report;
            if (isTm)
            {
                ITmExchange exchange = ext == ".tmx" ? new TmxExchange() : new TmXlsxExchange();
                report = await exchange.ImportAsync(importPath, opened.Tm, CancellationToken.None);
            }
            else
            {
                ITbExchange exchange = ext == ".tbx" ? new TbxExchange() : new TbXlsxExchange();
                report = await exchange.ImportAsync(importPath, opened.Tb, CancellationToken.None);
            }

            ContentDialog.Ask(this, $"Import {caption}", ReportText(report), "OK", null);
        }
    }

    private async Task ExportAsync(string caption, string ext, bool isTm, bool isTb)
    {
        var dialog = new SaveFileDialog
        {
            Title = $"Export {caption}",
            DefaultExtension = ext.TrimStart('.'),
            Filters = { new FileDialogFilter { Name = caption, Extensions = { ext.TrimStart('.') } } },
        };
        if (dialog.ShowAsync(this).GetAwaiter().GetResult() is not { } exportPath || _project is null)
        {
            return;
        }

        var opened = ProjectFolder.OpenStores(_folder, _project.SourceLanguage, _project.TargetLanguage);
        using (opened.Tm)
        using (opened.Tb)
        {
            if (isTm)
            {
                ITmExchange exchange = ext == ".tmx" ? new TmxExchange() : new TmXlsxExchange();
                await exchange.ExportAsync(exportPath, opened.Tm, CancellationToken.None);
            }
            else
            {
                ITbExchange exchange = ext == ".tbx" ? new TbxExchange() : new TbXlsxExchange();
                await exchange.ExportAsync(exportPath, opened.Tb, CancellationToken.None);
            }
        }

        ContentDialog.Ask(this, $"Export {caption}", "Exported.", "OK", null);
    }

    private static string ReportText(ImportReport report)
        => report.IsError
            ? $"Error: {report.Error}"
            : $"Added: {report.Added}  Updated: {report.Updated}  Skipped: {report.Skipped}" +
              (report.TagStripped > 0 ? $"  ({report.TagStripped} inline tags stripped)" : "");

    private void SaveProject()
    {
        _project?.Save();
        Reload();
    }
}