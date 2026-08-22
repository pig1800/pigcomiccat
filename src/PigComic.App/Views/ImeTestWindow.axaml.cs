namespace PigComic.App.Views;

public partial class ImeTestWindow : Avalonia.Controls.Window
{
    public ImeTestWindow()
    {
        InitializeComponent();
        Single.ConfirmRequested += (_, _) => LogConfirm("Single");
        Multi.ConfirmRequested += (_, _) => LogConfirm("Multi");
        Closing += (_, _) => PromptToSaveChecklist();
    }

    private int _confirmCount;

    private void LogConfirm(string which)
    {
        _confirmCount++;
        ConfirmLog.Text = $"Confirm fired in {which} (count {_confirmCount}). " +
                          "If this happened during composition, §21 item 4 FAILS — do not start M5.";
    }

    private void PromptToSaveChecklist()
    {
        // Guidance only; the authoritative record is docs/IME_REPORT.md, edited by hand.
    }
}