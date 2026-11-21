using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Larmcentralen.Client.UI.Services;

public static class UiServiceCollectionExtensions
{
    /// <summary>
    /// Registers UI-only state. The host also registers an <see cref="IFileSaveService"/> and may
    /// register an <see cref="IThemeStore"/> before calling this.
    /// </summary>
    public static IServiceCollection AddLarmcentralenUi(this IServiceCollection services)
    {
        services.AddSingleton<SearchState>();
        services.TryAddSingleton<IThemeStore, InMemoryThemeStore>();
        return services;
    }
}
