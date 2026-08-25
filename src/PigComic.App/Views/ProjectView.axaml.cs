using Avalonia.Controls;
using Avalonia.Interactivity;
using PigComic.App.Services;
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

    private async void OnAddChapterClick(object? sender, RoutedEventArgs e)
    {
        var picked = await FilePickers.OpenFileAsync(
            this, "Add .pcml chapter", "PigComic chapter", "pcml").ConfigureAwait(true);
        if (picked is not null)
        {
            _project?.AddChapter(picked);
            SaveProject();
        }
    }

    private async void OnRemoveChapterClick(object? sender, RoutedEventArgs e)
    {
        if (ChapterList.SelectedItem is not ChapterRow row)
        {
            return;
        }

        if (await ContentDialog.AskAsync(this, "Remove chapter from project?",
                "The .pcml file itself is never deleted.", "Remove", "Cancel"))
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

        var editor = new EditorView(row.Path, _folder);
        editor.Show();
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
            ("Import TM… (TMX)", () => ImportAsync("TM (TMX)", ".tmx", true)),
            ("Export TM… (TMX)", () => ExportAsync("TM (TMX)", ".tmx", true)),
            ("Import TM… (XLSX)", () => ImportAsync("TM (XLSX)", ".xlsx", true)),
            ("Export TM… (XLSX)", () => ExportAsync("TM (XLSX)", ".xlsx", true)),
            ("Import TB… (TBX)", () => ImportAsync("TB (TBX)", ".tbx", false)),
            ("Export TB… (TBX)", () => ExportAsync("TB (TBX)", ".tbx", false)),
            ("Import TB… (XLSX)", () => ImportAsync("TB (XLSX)", ".xlsx", false)),
            ("Export TB… (XLSX)", () => ExportAsync("TB (XLSX)", ".xlsx", false)),
        };
        foreach (var (header, run) in items)
        {
            var item = new MenuItem { Header = header, Tag = run };
            item.Click += async (_, _) => await ((Func<Task>)item.Tag!)();
            menu.Items.Add(item);
        }

        menu.Open();
    }

    private async Task ImportAsync(string caption, string ext, bool isTm)
    {
        if (_project is null)
        {
            return;
        }

        var picked = await FilePickers.OpenFileAsync(
            this, $"Import {caption}", caption, ext.TrimStart('.')).ConfigureAwait(true);
        if (picked is null)
        {
            return;
        }

        var opened = ProjectFolder.OpenStores(_folder, _project.SourceLanguage, _project.TargetLanguage);
        using (opened.Tm)
        using (opened.Tb)
        {
            ImportReport report;
            if (isTm)
            {
                ITmExchange exchange = ext == ".tmx" ? new TmxExchange() : new TmXlsxExchange();
                report = await exchange.ImportAsync(picked, opened.Tm, CancellationToken.None);
            }
            else
            {
                ITbExchange exchange = ext == ".tbx" ? new TbxExchange() : new TbXlsxExchange();
                report = await exchange.ImportAsync(picked, opened.Tb, CancellationToken.None);
            }

            await ContentDialog.AskAsync(this, $"Import {caption}", ReportText(report), "OK", null);
        }
    }

    private async Task ExportAsync(string caption, string ext, bool isTm)
    {
        if (_project is null)
        {
            return;
        }

        var path = await FilePickers.SaveFileAsync(
            this, $"Export {caption}", caption, ext.TrimStart('.')).ConfigureAwait(true);
        if (path is null)
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
                await exchange.ExportAsync(path, opened.Tm, CancellationToken.None);
            }
            else
            {
                ITbExchange exchange = ext == ".tbx" ? new TbxExchange() : new TbXlsxExchange();
                await exchange.ExportAsync(path, opened.Tb, CancellationToken.None);
            }
        }

        await ContentDialog.AskAsync(this, $"Export {caption}", "Exported.", "OK", null);
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