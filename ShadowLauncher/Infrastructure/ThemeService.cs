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
    private static readonly ThemeDefinition[] Themes =
    [
        new("Shadow",    "Presentation/Themes/ShadowTheme.xaml"),
        new("Shadowfire", "Presentation/Themes/ShadowfireTheme.xaml"),
        new("Nether",    "Presentation/Themes/NetherTheme.xaml"),
        new("Classic",   "Presentation/Themes/ClassicTheme.xaml"),
    ];

    private int _currentIndex;
    private ResourceDictionary? _activeThemeDict;

    public ThemeService(IConfigurationProvider config)
    {
        var saved = config.Theme;
        var idx = Array.FindIndex(Themes, t => t.Name == saved);
        _currentIndex = idx >= 0 ? idx : 0;
    }

    public string CurrentThemeName => Themes[_currentIndex].Name;
    public int ThemeCount => Themes.Length;

    public event Action<string>? ThemeChanged;

    /// <summary>Applies the theme that was loaded from config on construction.</summary>
    public void ApplySaved() => Apply(_currentIndex);

    public void Next()    => Apply((_currentIndex + 1) % Themes.Length);
    public void Previous() => Apply((_currentIndex - 1 + Themes.Length) % Themes.Length);

    private void Apply(int index)
    {
        _currentIndex = index;
        var def = Themes[_currentIndex];

        var newDict = new ResourceDictionary
        {
            Source = new Uri(def.Uri, UriKind.Relative)
        };

        // Replace only our own theme dictionary so other merged dictionaries (if any)
        // are preserved across theme switches.
        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
        if (_activeThemeDict is not null)
        {
            var existingIndex = merged.IndexOf(_activeThemeDict);
            if (existingIndex >= 0)
                merged[existingIndex] = newDict;
            else
                merged.Add(newDict);
        }
        else
        {
            merged.Add(newDict);
        }
        _activeThemeDict = newDict;

        ThemeChanged?.Invoke(def.Name);
    }

    private record ThemeDefinition(string Name, string Uri);
}
