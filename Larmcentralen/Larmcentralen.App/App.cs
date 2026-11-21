using Microsoft.AspNetCore.Components.WebView.Maui;

namespace Larmcentralen.App;

/// <summary>One window, one BlazorWebView. No XAML pages: every screen is a Razor component.</summary>
public class App : Application
{
    public App()
    {
        UserAppTheme = AppTheme.Light;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var webView = new BlazorWebView { HostPage = "wwwroot/index.html" };
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
