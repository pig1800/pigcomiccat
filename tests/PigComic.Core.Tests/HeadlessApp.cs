using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

namespace PigComic.Core.Tests;

/// <summary>Shared headless Avalonia app for render-sensitive tests (SPEC §21 IME rendering).</summary>
public static class HeadlessApp
{
    private static readonly object Gate = new();
    private static bool _started;

    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_started)
            {
                return;
            }

            AppBuilder.Configure<HeadlessApplication>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .AfterSetup(_ => { })
                .SetupWithoutStarting();
            _started = true;
        }
    }

    private sealed class HeadlessApplication : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
        }
    }
}
