namespace PigComic.Core.Adapters;

/// <summary>
/// Default <see cref="ILlmClient"/> registration. The app must build and run
/// fully with this stub — LLM QA is an on-demand, optional feature (SPEC §25.1).
/// </summary>
public sealed class StubLlmClient : ILlmClient
{
    public Task<string> CompleteAsync(LlmRequest request, IProgress<string>? progress, CancellationToken ct)
    {
        throw new LlmNotConfiguredException(
            "LLM not configured: no provider adapter is registered. " +
            "Install PigComic.Adapters.PigTranslate (PigComic.Full.sln) or configure an LLM in project settings.");
    }
}