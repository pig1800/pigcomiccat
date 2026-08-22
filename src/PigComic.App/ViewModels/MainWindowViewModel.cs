using CommunityToolkit.Mvvm.ComponentModel;

namespace PigComic.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string title = "PigComic";
}