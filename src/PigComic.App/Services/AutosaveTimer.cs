using Avalonia.Threading;

namespace PigComic.App.Services;

/// <summary>
/// M5.6 autosave timer (SPEC §5.5/§6.2): ticks every <c>autosaveSeconds</c>
/// (default 180) and runs the atomic save only when the chapter is dirty.
/// </summary>
public sealed class AutosaveTimer : IDisposable
{
    private readonly ChapterSession _session;
    private readonly DispatcherTimer _timer;

    public AutosaveTimer(ChapterSession session, int intervalSeconds)
    {
        _session = session;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(5, intervalSeconds)) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>Raised after a successful autosave (status bar "Saved HH:mm").</summary>
    public event Action<DateTime>? Saved;

    private async void OnTick(object? sender, EventArgs e)
    {
        if (!_session.IsDirty)
        {
            return;
        }

        try
        {
            await _session.SaveAsync(CancellationToken.None);
            Saved?.Invoke(DateTime.Now);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Autosave failed: {ex.Message}");
        }
    }

    public void Dispose() => _timer.Stop();
}