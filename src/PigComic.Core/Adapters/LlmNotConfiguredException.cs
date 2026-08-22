namespace PigComic.Core.Adapters;

/// <summary>
/// Thrown by <see cref="StubLlmClient"/> (and any real client that is not configured)
/// to signal that no LLM provider is available. The UI catches this and shows
/// "LLM not configured" guidance. The app is fully functional without an LLM.
/// </summary>
public class LlmNotConfiguredException : InvalidOperationException
{
    public LlmNotConfiguredException(string message) : base(message)
    {
    }
}