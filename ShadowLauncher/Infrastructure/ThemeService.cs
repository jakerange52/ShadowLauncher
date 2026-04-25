using System.Windows;
using ShadowLauncher.Core.Interfaces;

namespace ShadowLauncher.Infrastructure;

/// <summary>
/// Manages the active UI theme. Themes are ResourceDictionary XAML files under
/// Presentation/Themes/. Switching a theme merges the new dictionary into
/// Application.Resources at runtime — all controls update immediately.
/// </summary>
public class ThemeService
{
    private static readonly IReadOnlyList<ThemeDefinition> Themes =
    [
        new("Shadow",    "Presentation/Themes/ShadowTheme.xaml"),
        new("LostLight", "Presentation/Themes/LostLightTheme.xaml"),
        new("Nether",    "Presentation/Themes/NetherTheme.xaml"),
        new("Classic",   "Presentation/Themes/ClassicTheme.xaml"),
    ];

    private int _currentIndex;

    public ThemeService(IConfigurationProvider config)
    {
        // Restore saved theme, defaulting to Shadow.
        var saved = config.Theme;
        var match = Themes.FirstOrDefault(t => t.Name == saved) ?? Themes[0];
        _currentIndex = Themes.ToList().IndexOf(match);
    }

    public string CurrentThemeName => Themes[_currentIndex].Name;
    public int ThemeCount => Themes.Count;

    public event Action<string>? ThemeChanged;

    /// <summary>Applies the theme that was loaded from config on construction.</summary>
    public void ApplySaved() => Apply(_currentIndex);

    public void Next()    => Apply((_currentIndex + 1) % Themes.Count);
    public void Previous() => Apply((_currentIndex - 1 + Themes.Count) % Themes.Count);

    private void Apply(int index)
    {
        _currentIndex = index;
        var def = Themes[_currentIndex];

        var dict = new ResourceDictionary
        {
            Source = new Uri(def.Uri, UriKind.Relative)
        };

        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(dict);

        ThemeChanged?.Invoke(def.Name);
    }

    private record ThemeDefinition(string Name, string Uri);
}
