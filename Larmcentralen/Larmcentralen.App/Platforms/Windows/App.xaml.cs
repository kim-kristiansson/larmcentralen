using Microsoft.UI.Xaml;

namespace Larmcentralen.App.WinUI;

/// <summary>The WinUI entry point. All app setup lives in <see cref="MauiProgram"/>.</summary>
public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
