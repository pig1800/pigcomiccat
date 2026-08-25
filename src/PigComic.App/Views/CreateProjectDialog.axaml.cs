using Avalonia.Controls;
using Avalonia.Interactivity;
using PigComic.App.Services;
using PigComic.Core.Project;

namespace PigComic.App.Views;

public partial class CreateProjectDialog : Avalonia.Controls.Window
{
    public string? CreatedProjectPath { get; private set; }

    public CreateProjectDialog()
    {
        InitializeComponent();
    }

    private void OnFieldChanged(object? sender, TextChangedEventArgs e) => Validate();

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var picked = await FilePickers.OpenFolderAsync(this, "Project folder").ConfigureAwait(true);
        if (!string.IsNullOrEmpty(picked))
        {
            FolderBox.Text = picked;
            Validate();
        }
    }

    private void Validate()
    {
        var title = TitleBox.Text?.Trim() ?? "";
        var folder = FolderBox.Text?.Trim() ?? "";
        var ok = title.Length > 0 && folder.Length > 0;
        var error = "";
        if (folder.Length > 0 && Directory.Exists(folder) &&
            Directory.EnumerateFileSystemEntries(folder).Any())
        {
            ok = false;
            error = "Folder must be empty or nonexistent.";
        }

        ErrorText.Text = error;
        OkButton.IsEnabled = ok;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private string SourceLang() => SourceCombo.SelectedItem is ComboBoxItem { Content: string s } && s != "(free text)" ? s : "zh-CN";

    private string TargetLang() => TargetCombo.SelectedItem is ComboBoxItem { Content: string s } && s != "(free text)" ? s : "ja";

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text!.Trim();
        var folder = FolderBox.Text!.Trim();
        if (title.Length == 0 || folder.Length == 0)
        {
            return;
        }

        try
        {
            ProjectFolder.Create(folder, title, SourceLang(), TargetLang());
            CreatedProjectPath = Path.Combine(folder, "project.json");
            Close(true);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }
}