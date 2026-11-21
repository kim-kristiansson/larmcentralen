using Larmcentralen.Client.Core.Api;
using Larmcentralen.Client.Core.Storage;
using Larmcentralen.Client.Core.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Larmcentralen.Client.Core;

public static class ServiceCollectionExtensions
{
    private const string HttpClientName = "LarmApi";

    /// <summary>
    /// Registers the API client, local store, recent alarms and the knowledge base.
    /// A host can register its own <see cref="INetworkStatus"/>; the default assumes the network is up.
    /// </summary>
    public static IServiceCollection AddLarmcentralenClient(
        this IServiceCollection services, string databasePath, Action<ApiClientOptions>? configure = null)
    {
        var options = new ApiClientOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.AddHttpClient(HttpClientName, http =>
        {
            http.BaseAddress = options.BaseAddress;
            http.Timeout = options.Timeout;
        });

        // One long-lived client for a desktop app. KnowledgeBase is a singleton and owns the connection state.
        services.TryAddSingleton(sp => new LarmApiClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));
        services.TryAddSingleton(_ => new LocalStore(databasePath));
        services.TryAddSingleton<RecentAlarms>();
        services.TryAddSingleton<INetworkStatus, AlwaysOnlineNetworkStatus>();
        services.TryAddSingleton<KnowledgeBase>();
        return services;
    }
}
