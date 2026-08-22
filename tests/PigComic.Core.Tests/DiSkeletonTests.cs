using Microsoft.Extensions.DependencyInjection;
using PigComic.App.Services;
using PigComic.Core.Adapters;
using Xunit;

namespace PigComic.Core.Tests;

public class DiSkeletonTests
{
    [Fact]
    public void Resolve_LlmClient_From_Provider()
    {
        var provider = ServiceRegistry.CreateProvider();
        var client = provider.GetRequiredService<ILlmClient>();
        Assert.IsType<StubLlmClient>(client);
    }

    [Fact]
    public async Task StubLlmClient_Throws_LlmNotConfiguredException()
    {
        var provider = ServiceRegistry.CreateProvider();
        var client = provider.GetRequiredService<ILlmClient>();
        var request = new LlmRequest("claude", "claude-opus-5", "sys", "user");

        await Assert.ThrowsAsync<LlmNotConfiguredException>(
            () => client.CompleteAsync(request, null, CancellationToken.None));
    }
}