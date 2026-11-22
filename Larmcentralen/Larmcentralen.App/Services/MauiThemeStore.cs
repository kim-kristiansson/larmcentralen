using Larmcentralen.Client.UI.Services;

namespace Larmcentralen.App.Services;

/// <summary>
/// Stores the theme in MAUI Preferences and keeps the native side in step with it: the MAUI
/// window chrome (UserAppTheme) and the WebView2 colour scheme, so Auto follows Windows and
/// Ljust/Mörkt also affect native parts such as select popups and scrollbars.
/// </summary>
public sealed class MauiThemeStore : IThemeStore
{
    private const string Key = "theme";

#if WINDOWS
    private Microsoft.UI.Xaml.Controls.WebView2? _webView;
#endif

    public ThemePreference Load() =>
        Enum.TryParse<ThemePreference>(Preferences.Default.Get(Key, nameof(ThemePreference.Auto)), out var value)
        && Enum.IsDefined(value)
            ? value
            : ThemePreference.Auto;

    public void Save(ThemePreference preference)
    {
        Preferences.Default.Set(Key, preference.ToString());
        ApplyNative(preference);
    }

    /// <summary>The MAUI theme for a preference. Unspecified means follow the OS.</summary>
    public static AppTheme ToAppTheme(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => AppTheme.Light,
        ThemePreference.Dark => AppTheme.Dark,
        _ => AppTheme.Unspecified
    };

#if WINDOWS
    /// <summary>Called when the BlazorWebView's WebView2 is ready.</summary>
    public void Attach(Microsoft.UI.Xaml.Controls.WebView2 webView)
    {
        _webView = webView;
        ApplyNative(Load());
    }
#endif

    private void ApplyNative(ThemePreference preference) => MainThread.BeginInvokeOnMainThread(() =>
    {
        // The theme is cosmetic: an exception here would be unobserved on the dispatcher and could
        // take the app down (for example an old WebView2 runtime without Profile.PreferredColorScheme).
        try
        {
            if (Application.Current is { } app) app.UserAppTheme = ToAppTheme(preference);
#if WINDOWS
            if (_webView?.CoreWebView2 is { } core)
            {
                core.Profile.PreferredColorScheme = preference switch
                {
                    ThemePreference.Light => Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light,
                    ThemePreference.Dark => Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark,
                    _ => Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Auto
                };
            }
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not apply the theme natively: {ex}");
        }
    });
}
