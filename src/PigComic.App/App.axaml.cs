using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PigComic.App.Services;
using PigComic.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace PigComic.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var services = ServiceRegistry.CreateProvider();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new ViewModels.MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}