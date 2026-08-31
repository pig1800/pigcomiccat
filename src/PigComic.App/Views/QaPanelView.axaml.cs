using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PigComic.App.ViewModels;

namespace PigComic.App.Views;

/// <summary>Dockable bottom QA panel (M8.3, SPEC §12): issue list; double-click navigates.</summary>
public partial class QaPanelView : UserControl
{
    public QaPanelView()
    {
        InitializeComponent();
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: QaPanelRow row } && DataContext is QaPanelViewModel vm)
        {
            vm.RequestNavigate(row.BubbleId);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        if (DataContext is QaPanelViewModel vm)
        {
            vm.Close();
        }
    }
}