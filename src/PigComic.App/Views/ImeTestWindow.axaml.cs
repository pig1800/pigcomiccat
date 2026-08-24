using System.Text.Json;
using Avalonia.Interactivity;
using PigComic.App.Ime;

namespace PigComic.App.Views;

public partial class ImeTestWindow : Avalonia.Controls.Window
{
    public ImeTestWindow()
    {
        InitializeComponent();
        Single.ConfirmRequested += (_, _) => LogConfirm("Single");
        Multi.ConfirmRequested += (_, _) => LogConfirm("Multi");
        DiagPath.Text = ImeMessageMonitor.DiagnosticsPath;
        DiagToggle.IsChecked = ImeMessageMonitor.DiagnosticsEnabled;
        CaptureToggle.IsChecked = ImeMessageMonitor.CaptureEnabled;
        Opened += (_, _) => UpdateHookStatus();
        Closing += (_, _) => ImeMessageMonitor.DiagnosticsEnabled = false;
    }

    private void UpdateHookStatus()
        => HookStatus.Text = ImeMessageMonitor.IsAttached(this)
            ? $"Composition hook installed ({ImeMessageMonitor.AttachedCount} window(s))."
            : "Composition hook NOT installed — clause data cannot be captured.";

    private void OnCaptureToggled(object? sender, RoutedEventArgs e)
    {
        ImeMessageMonitor.CaptureEnabled = CaptureToggle.IsChecked == true;
        UpdateHookStatus();
    }

    private int _confirmCount;

    private void LogConfirm(string which)
    {
        _confirmCount++;
        ConfirmLog.Text = $"Confirm fired in {which} (count {_confirmCount}). " +
                          "If this happened during composition, §21 item 4 FAILS — do not start M5.";
    }

    private void OnDiagToggled(object? sender, RoutedEventArgs e)
    {
        ImeMessageMonitor.DiagnosticsEnabled = DiagToggle.IsChecked == true;
        DiagStatus.Text = ImeMessageMonitor.DiagnosticsEnabled
            ? "Recording. Compose in the editors on the left, then press Summarise."
            : "Not recording.";
    }

    /// <summary>
    /// Reduces the raw JSONL capture to the one question PLAN M2.6 asks: does this IME
    /// actually deliver clause and attribute bytes over IMM32?
    /// </summary>
    private void OnSummariseDiag(object? sender, RoutedEventArgs e)
    {
        var path = ImeMessageMonitor.DiagnosticsPath;
        if (!File.Exists(path))
        {
            DiagStatus.Text = "No log file yet.";
            return;
        }

        int lines = 0, withClause = 0, withAttr = 0, maxClause = 0, maxAttr = 0;
        var lastText = "";

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                lines++;

                var bytes = root.GetProperty("bytes");
                var clause = bytes.GetProperty("clause").GetInt32();
                var attr = bytes.GetProperty("attr").GetInt32();

                if (clause > 0) { withClause++; }
                if (attr > 0) { withAttr++; }
                maxClause = Math.Max(maxClause, clause);
                maxAttr = Math.Max(maxAttr, attr);

                if (root.TryGetProperty("text", out var text))
                {
                    lastText = text.GetString() ?? "";
                }
            }
            catch (JsonException)
            {
                // Ignore a truncated trailing line.
            }
        }

        var verdict = withClause > 0 || withAttr > 0
            ? "IMM32 IS delivering henkan data — the modern rendering should work for this IME."
            : "NO clause/attr bytes seen. If this was a real JA conversion (kana → Space), this IME " +
              "gives nothing over IMM32 — escalate per docs/IME_MODERN_COMPOSITION.md §5.";

        DiagStatus.Text =
            $"{lines} composition messages · clause bytes in {withClause} (max {maxClause}) · " +
            $"attr bytes in {withAttr} (max {maxAttr}) · last text: 「{lastText}」\n{verdict}";
    }

    private void OnClearDiag(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(ImeMessageMonitor.DiagnosticsPath))
            {
                File.Delete(ImeMessageMonitor.DiagnosticsPath);
            }

            DiagStatus.Text = "Log cleared.";
        }
        catch (IOException ex)
        {
            DiagStatus.Text = $"Could not clear: {ex.Message}";
        }
    }
}
