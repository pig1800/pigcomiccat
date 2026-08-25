using Avalonia.Controls;
using Avalonia.Input;

namespace PigComic.App.Views;

/// <summary>M5.5 results list: double-click inserts the entry (SPEC §9).</summary>
public partial class MatchListView : UserControl
{
    public MatchListView()
    {
        InitializeComponent();
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not ViewModels.MatchListViewModel vm ||
            MatchList.SelectedItem is not ViewModels.MatchRowViewModel row)
        {
            return;
        }

        vm.Insert(row.Number);
    }
}