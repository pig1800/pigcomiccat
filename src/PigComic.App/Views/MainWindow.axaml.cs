using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PigComic.App.ViewModels;

namespace PigComic.App.Views;

public partial class MainWindow : Avalonia.Controls.Window
{
    public ProjectListViewModel Vm { get; }

    public MainWindow()
    {
        InitializeComponent();
        Vm = new ProjectListViewModel();
        DataContext = Vm;
        ProjectList.SelectionChanged += (_, _) =>
        {
            if (ProjectList.SelectedItem is ProjectListItemViewModel item)
            {
                OpenProject(item);
            }
        };
    }

    private void OnSpikeClick(object? sender, RoutedEventArgs e) => new SpikeWindow().Show();

    private void OnImeClick(object? sender, RoutedEventArgs e) => new ImeTestWindow().Show();

    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new Avalonia.Controls.OpenFileDialog
        {
            Title = "Open project.json",
            AllowMultiple = false,
            Filters = { new Avalonia.Controls.FileDialogFilter { Name = "PigComic project", Extensions = { "json" } } },
        };
        var picked = dialog.ShowAsync(this).GetAwaiter().GetResult();
        if (picked is { Length: > 0 })
        {
            Vm.AddProjectFile(picked[0]);
        }
    }

    private void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new CreateProjectDialog();
        if (dialog.ShowDialog<bool?>(this).GetAwaiter().GetResult() == true && dialog.CreatedProjectPath is { } path)
        {
            Vm.AddProjectFile(path);
        }
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Avalonia.Controls.Button)?.Tag is not string path)
        {
            return;
        }

        var dialog = new RemoveProjectDialog(path, Path.GetDirectoryName(path));
        if (dialog.ShowDialog<bool?>(this).GetAwaiter().GetResult() == true && dialog.DeleteFolder)
        {
            Vm.RemoveProject(path, dialog.DeleteFolder);
        }
        else if (dialog.Outcome == RemoveProjectDialogOutcome.RemoveFromList)
        {
            Vm.RemoveProject(path, deleteFolder: false);
        }
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ProjectList.SelectedItem is ProjectListItemViewModel item)
        {
            OpenProject(item);
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && ProjectList.SelectedItem is ProjectListItemViewModel item)
        {
            OpenProject(item);
            e.Handled = true;
        }
    }

    private void OpenProject(ProjectListItemViewModel item)
    {
        if (File.Exists(item.ProjectJsonPath))
        {
            var project = PigComic.Core.Project.ProjectFile.Load(item.ProjectJsonPath);
            var missing = project.ChapterPaths.Where(p => !File.Exists(p)).ToList();
            if (missing.Count > 0)
            {
                var relink = new RelinkDialog(missing);
                if (relink.ShowDialog<bool?>(this).GetAwaiter().GetResult() == true && !relink.Cancelled)
                {
                    foreach (var (m, r) in missing.Zip(relink.ResolvedPaths))
                    {
                        _ = r; // resolved value used to rewrite below
                        project.RemoveChapter(m);
                        project.AddChapter(r);
                    }

                    project.Save();
                }
                else
                {
                    return; // Cancel → stay on the main project list (SPEC §6.6)
                }
            }
        }

        var projectView = new ProjectView(item.ProjectJsonPath);
        projectView.Show();
    }
}