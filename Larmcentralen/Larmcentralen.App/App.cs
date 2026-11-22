using Larmcentralen.App.Services;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace Larmcentralen.App;

/// <summary>One window, one BlazorWebView. No XAML pages: every screen is a Razor component.</summary>
public class App : Application
{
    private readonly MauiThemeStore _themes;

    public App(MauiThemeStore themes)
    {
        _themes = themes;
        // Auto leaves this Unspecified so the window follows Windows; Ljust and Mörkt force it.
        UserAppTheme = MauiThemeStore.ToAppTheme(themes.Load());
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var webView = new BlazorWebView { HostPage = "wwwroot/index.html" };
#if WINDOWS
        // The window lives as long as the app, so this handler needs no unsubscribe.
        webView.BlazorWebViewInitialized += (_, e) => _themes.Attach(e.WebView);
#endif
        webView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Larmcentralen.Client.UI.App)
        });

        return new Window(new ContentPage { Content = webView })
        {
            Title = "Larmcentralen",
            MinimumWidth = 720,
            MinimumHeight = 560
        };
    }
}
