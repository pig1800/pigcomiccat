using Avalonia;
using Avalonia.Dialogs;
using System;

namespace PigComic.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Non-interactive self-check for UI/theme wiring — see SmokeTest.
        if (args.Contains("--smoke"))
        {
            Environment.Exit(SmokeTest.Run());
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}