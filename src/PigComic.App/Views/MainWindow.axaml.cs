using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PigComic.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnSpikeClick(object? sender, RoutedEventArgs e)
    {
        new SpikeWindow { }.Show();
    }

    private void OnImeClick(object? sender, RoutedEventArgs e)
    {
        new ImeTestWindow { }.Show();
    }
}