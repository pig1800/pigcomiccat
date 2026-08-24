using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PigComic.App.Services;
using PigComic.App.ViewModels;

namespace PigComic.App.Views;

public partial class MainWindow : Window
{
    public ProjectListViewModel Vm { get; }

    private readonly HashSet<string> _openProjectViews = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();
        Vm = new ProjectListViewModel();
        DataContext = Vm;
    }

    private void OnSpikeClick(object? sender, RoutedEventArgs e) => new SpikeWindow().Show();

    private void OnImeClick(object? sender, RoutedEventArgs e) => new ImeTestWindow().Show();

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var picked = await FilePickers.OpenFileAsync(
            this, "Open project.json", "PigComic project", "json").ConfigureAwait(true);
        if (picked is not null)
        {
            Vm.AddProjectFile(picked);
        }
    }

    private async void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new CreateProjectDialog();
        var created = await dialog.ShowDialog<bool?>(this).ConfigureAwait(true);
        if (created == true && dialog.CreatedProjectPath is { } path)
        {
            Vm.AddProjectFile(path);
        }
    }

    private async void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string path)
        {
            return;
        }

        var dialog = new RemoveProjectDialog(path, Path.GetDirectoryName(path));
        var result = await dialog.ShowDialog<bool?>(this).ConfigureAwait(true);
        if (result != true)
        {
            return;
        }

        if (dialog.Outcome == RemoveProjectDialogOutcome.DeleteFolder &&
            dialog.Folder is { } folder && Directory.Exists(folder))
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex)
            {
                await ContentDialog.AskAsync(this, "Delete failed", ex.Message, "OK", null);
                return;
            }
        }

        Vm.RemoveProject(path);
    }

    private async void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ProjectList.SelectedItem is ProjectListItemViewModel item)
        {
            await OpenProjectAsync(item);
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.Key is Key.Enter or Key.Space) && ProjectList.SelectedItem is ProjectListItemViewModel item)
        {
            e.Handled = true;
            _ = OpenProjectAsync(item);
        }
    }

    private async Task OpenProjectAsync(ProjectListItemViewModel item)
    {
        if (!_openProjectViews.Add(item.ProjectJsonPath))
        {
            return; // already open — don't stack windows
        }

        try
        {
            if (File.Exists(item.ProjectJsonPath))
            {
                var project = PigComic.Core.Project.ProjectFile.Load(item.ProjectJsonPath);
                var missing = project.ChapterPaths.Where(p => !File.Exists(p)).ToList();
                if (missing.Count > 0)
                {
                    var relink = new RelinkDialog(missing);
                    var ok = await relink.ShowDialog<bool?>(this).ConfigureAwait(true);
                    if (ok != true || relink.Cancelled)
                    {
                        return; // Cancel → stay on the project list (SPEC §6.6)
                    }

                    foreach (var (m, r) in missing.Zip(relink.ResolvedPaths))
                    {
                        project.RemoveChapter(m);
                        project.AddChapter(r);
                    }

                    project.Save();
                }
            }

            var view = new ProjectView(item.ProjectJsonPath);
            view.Closed += (_, _) => _openProjectViews.Remove(item.ProjectJsonPath);
            view.Show();
        }
        catch (Exception ex)
        {
            _openProjectViews.Remove(item.ProjectJsonPath);
            await ContentDialog.AskAsync(this, "Open project", ex.Message, "OK", null);
        }
    }
}