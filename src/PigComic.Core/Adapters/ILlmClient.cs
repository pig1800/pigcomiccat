namespace PigComic.Core.Adapters;

public sealed record LlmRequest(
    string Provider,
    string Model,
    string SystemPrompt,
    string UserContent,
    double Temperature = 0.2);

public interface ILlmClient
{
    Task<string> CompleteAsync(LlmRequest request, IProgress<string>? progress, CancellationToken ct);
}