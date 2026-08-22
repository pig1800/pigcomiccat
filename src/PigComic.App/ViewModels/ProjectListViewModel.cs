using CommunityToolkit.Mvvm.ComponentModel;
using PigComic.Core.Project;
using System.Collections.ObjectModel;

namespace PigComic.App.ViewModels;

public partial class ProjectListItemViewModel : ObservableObject
{
    public required string ProjectJsonPath { get; init; }

    [ObservableProperty]
    private string title = "";

    [ObservableProperty]
    private string subtitle = "";

    /// <summary>Resolves display data by reading the project file (missing files show a warning).</summary>
    public void Refresh()
    {
        if (!File.Exists(ProjectJsonPath))
        {
            Title = System.IO.Path.GetFileNameWithoutExtension(ProjectJsonPath);
            Subtitle = "⚠ file missing";
            return;
        }

        try
        {
            var pf = ProjectFile.Load(ProjectJsonPath);
            Title = pf.Title.Length > 0 ? pf.Title : System.IO.Path.GetFileNameWithoutExtension(ProjectJsonPath);
            Subtitle = $"{pf.SourceLanguage} → {pf.TargetLanguage} · {pf.ChapterPaths.Count} chapters";
        }
        catch (Exception)
        {
            Title = System.IO.Path.GetFileNameWithoutExtension(ProjectJsonPath);
            Subtitle = "unreadable";
        }
    }
}

public partial class ProjectListViewModel : ObservableObject
{
    private readonly ProjectRegistry _registry;

    public ObservableCollection<ProjectListItemViewModel> Projects { get; } = [];

    public ProjectListViewModel(ProjectRegistry? registry = null)
    {
        _registry = registry ?? new ProjectRegistry();
        Reload();
    }

    public ProjectRegistry Registry => _registry;

    public void Reload()
    {
        Projects.Clear();
        foreach (var path in _registry.ProjectPaths)
        {
            var item = new ProjectListItemViewModel { ProjectJsonPath = path };
            item.Refresh();
            Projects.Add(item);
        }
    }

    /// <summary>Adds a project from an external picker result and reloads.</summary>
    public void AddProjectFile(string projectJsonPath)
    {
        _registry.AddOrTouch(projectJsonPath);
        Reload();
    }

    public void RemoveProject(string projectJsonPath, bool deleteFolder)
    {
        // Permanent folder deletion happens in the dialog (D-08); registry removal here.
        _registry.Remove(projectJsonPath);
        Reload();
    }
}