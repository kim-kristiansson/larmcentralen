using Microsoft.Extensions.DependencyInjection;

namespace Larmcentralen.Client.UI.Services;

public static class UiServiceCollectionExtensions
{
    /// <summary>Registers UI-only state. The host also registers an <see cref="IFileSaveService"/>.</summary>
    public static IServiceCollection AddLarmcentralenUi(this IServiceCollection services)
    {
        services.AddSingleton<SearchState>();
        return services;
    }
}
