using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Maui;
using Larmcentralen.Client.Core;
using Larmcentralen.Client.Core.Sync;
using Larmcentralen.Client.UI.Services;
using Larmcentralen.App.Services;
using Microsoft.Extensions.Logging;

namespace Larmcentralen.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // The UI is Swedish; dates, sorting and number formats follow.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("sv-SE");

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit();

        builder.Services.AddMauiBlazorWebView();

        var settings = AppSettings.Load();
        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "larmcentralen.db");

        builder.Services.AddSingleton<INetworkStatus, MauiNetworkStatus>();
        builder.Services.AddSingleton<IFileSaveService, MauiFileSaveService>();
        builder.Services.AddLarmcentralenClient(databasePath, o =>
        {
            o.BaseAddress = settings.ApiBaseAddress;
            o.ApiKey = settings.ApiKey;
        });
        builder.Services.AddLarmcentralenUi();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
#endif

        return builder.Build();
    }
}

/// <summary>
/// Where the API lives. Read from appsettings.json in the app package, and overridable with the
/// LARMCENTRALEN_API_URL environment variable so a pilot PC can point at Azure without a rebuild.
/// The API key only comes from the LARMCENTRALEN_API_KEY environment variable: appsettings.json is
/// in git and ships inside the app, so a key must never go there. Operators' PCs get the read key;
/// admins get the admin key, which also shows the edit buttons.
/// </summary>
internal sealed record AppSettings(Uri ApiBaseAddress, string? ApiKey)
{
    private static readonly Uri Default = new("https://localhost:7094/");

    public static AppSettings Load() =>
        new(LoadBaseAddress(), Environment.GetEnvironmentVariable("LARMCENTRALEN_API_KEY"));

    private static Uri LoadBaseAddress()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("LARMCENTRALEN_API_URL");
        if (TryParse(fromEnvironment, out var envUri)) return envUri;

        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync("appsettings.json").GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("ApiBaseUrl", out var value) && TryParse(value.GetString(), out var fileUri))
                return fileUri;
        }
        catch (Exception ex) when (ex is FileNotFoundException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"appsettings.json not used: {ex.Message}");
        }
        return Default;
    }

    private static bool TryParse(string? value, out Uri uri)
    {
        uri = Default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!value.EndsWith('/')) value += "/";
        return Uri.TryCreate(value, UriKind.Absolute, out uri!);
    }
}
