using Microsoft.Extensions.DependencyInjection;
using PigComic.Core.Adapters;

namespace PigComic.App.Services;

/// <summary>
/// Central service wiring (SPEC: DI via Microsoft.Extensions.DependencyInjection).
/// </summary>
public static class ServiceRegistry
{
    public static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        Register(services);
        return services.BuildServiceProvider();
    }

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<ILlmClient, StubLlmClient>();
    }
}