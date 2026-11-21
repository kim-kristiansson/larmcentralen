namespace Larmcentralen.Client.UI.Services;

/// <summary>Which colour theme the user picked. Auto follows the operating system.</summary>
public enum ThemePreference
{
    Auto,
    Light,
    Dark
}

/// <summary>
/// Remembers the chosen theme between runs. The host implements it with whatever it has
/// (MAUI Preferences in the desktop app) and may also apply the theme to native chrome.
/// </summary>
public interface IThemeStore
{
    ThemePreference Load();
    void Save(ThemePreference preference);
}

/// <summary>Fallback for hosts that register nothing: the choice lives until the app closes.</summary>
internal sealed class InMemoryThemeStore : IThemeStore
{
    private ThemePreference _value;
    public ThemePreference Load() => _value;
    public void Save(ThemePreference preference) => _value = preference;
}
