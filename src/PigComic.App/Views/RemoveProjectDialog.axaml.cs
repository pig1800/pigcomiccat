namespace PigComic.App.Views;

public enum RemoveProjectDialogOutcome
{
    Cancelled,
    RemoveFromList,
    DeleteFolder,
}

public partial class RemoveProjectDialog : Avalonia.Controls.Window
{
    private readonly string _projectJsonPath;
    private readonly string? _folder;

    public RemoveProjectDialogOutcome Outcome { get; private set; } = RemoveProjectDialogOutcome.Cancelled;
    public string? Folder => _folder;

    public RemoveProjectDialog(string projectJsonPath, string? folder)
    {
        _projectJsonPath = projectJsonPath;
        _folder = folder;
        InitializeComponent();
        Hint.Text = $"Project: {projectJsonPath}" +
                    (folder is null ? "" : $"\nFolder: {folder}") +
                    "\n.pcml chapter files are never deleted.";
    }

    private void OnModeChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs _)
    {
        var deleting = DeleteRadio.IsChecked == true;
        ConfirmDelete.IsEnabled = deleting;
        OkButton.IsEnabled = !deleting || ConfirmDelete.IsChecked == true;
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(false);

    private void OnOkClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ListOnlyRadio.IsChecked == true)
        {
            Outcome = RemoveProjectDialogOutcome.RemoveFromList;
            Close(true);
            return;
        }

        if (DeleteRadio.IsChecked == true && ConfirmDelete.IsChecked == true)
        {
            Outcome = RemoveProjectDialogOutcome.DeleteFolder;
            Close(true);
        }
    }
}