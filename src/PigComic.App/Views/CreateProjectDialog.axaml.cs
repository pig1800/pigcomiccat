using Avalonia.Controls;
using Avalonia.Interactivity;
using PigComic.Core.Project;

namespace PigComic.App.Views;

public partial class CreateProjectDialog : Avalonia.Controls.Window
{
    public string? CreatedProjectPath { get; private set; }

    private string SelectedLanguage(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem { Content: string s })
        {
            if (s != "(free text)")
            {
                return s;
            }

            return FreeTextFor(combo) ?? s;
        }

        return "";
    }

    private string? FreeTextFor(ComboBox combo)
    {
        // Prompted via a small follow-up input in a real impl; keep the dialog simple:
        return combo.Tag?.ToString();
    }

    public CreateProjectDialog()
    {
        InitializeComponent();
    }

    private void OnFieldChanged(object? sender, TextChangedEventArgs e) => Validate();

    private void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Project folder" };
        var picked = dialog.ShowAsync(this).GetAwaiter().GetResult();
        if (picked is not null)
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

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text!.Trim();
        var folder = FolderBox.Text!.Trim();
        var src = SourceCombo.SelectedIndex >= 0 && SourceCombo.SelectedItem is ComboBoxItem { Content: string ss } && ss != "(free text)"
            ? ss
            : "ja";
        var tgt = TargetCombo.SelectedIndex >= 0 && TargetCombo.SelectedItem is ComboBoxItem { Content: string ts } && ts != "(free text)"
            ? ts
            : "zh-Hant";

        try
        {
            ProjectFolder.Create(folder, title, src, tgt);
            CreatedProjectPath = Path.Combine(folder, "project.json");
            Close(true);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }
}