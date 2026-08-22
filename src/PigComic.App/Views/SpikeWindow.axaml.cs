using System.Net;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PigComic.App.Controls;
using PigComic.App.Rendering;

namespace PigComic.App.Views;

public partial class SpikeWindow : Window
{
    private readonly string _stripsDir;

    public SpikeWindow()
    {
        InitializeComponent();
        _stripsDir = Path.Combine(AppContext.BaseDirectory, "strips");
        PathBox.Text = Path.Combine(_stripsDir, "strip.jpg");
        Closing += (_, _) => TiledView.Dispose();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => Hints.Text = $"FPS {TiledView.Fps:F0} (sustained ≥ 55 hz expected)";
        timer.Start();
    }

    private void OnLoadClick(object? sender, RoutedEventArgs e)
    {
        var path = PathBox.Text?.Trim() ?? "";
        if (path.Length == 0)
        {
            return;
        }

        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(_stripsDir, Path.GetFileName(path));
        }

        if (!File.Exists(path))
        {
            Hints.Text = $"Not found: {path}";
            return;
        }

        TiledView.SetImage(path);
        Hints.Text = $"Loaded {path}";
    }

    private void OnGenerateClick(object? sender, RoutedEventArgs e)
    {
        StripImageGenerator.Generate(_stripsDir);
        PathBox.Text = Path.Combine(_stripsDir, "strip.jpg");
        OnLoadClick(sender, e);
    }

    private void OnFitClick(object? sender, RoutedEventArgs e) => TiledView.FitWidth();

    private void OnZoomInClick(object? sender, RoutedEventArgs e)
        => TiledView.ZoomAbout(TiledView.Bounds.Width / 2, TiledView.Bounds.Height / 2, TiledView.Zoom * 1.2);

    private void OnZoomOutClick(object? sender, RoutedEventArgs e)
        => TiledView.ZoomAbout(TiledView.Bounds.Width / 2, TiledView.Bounds.Height / 2, TiledView.Zoom / 1.2);
}