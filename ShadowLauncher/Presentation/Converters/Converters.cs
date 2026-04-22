using System.Globalization;
using System.Windows.Data;

namespace ShadowLauncher.Presentation.Converters;

/// <summary>
/// Converts an integer (seconds) to a human-friendly duration like "1d 2h 35m" or "45m".
/// </summary>
public class UptimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int seconds || seconds <= 0)
            return "0m";

        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{ts.Minutes}m";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts website/discord URLs to visibility where website is shown only when non-empty and different from discord.
/// </summary>
public class WebsiteMenuVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var websiteUrl = values.Length > 0 ? values[0] as string : null;
        var discordUrl = values.Length > 1 ? values[1] as string : null;

        if (string.IsNullOrWhiteSpace(websiteUrl))
            return System.Windows.Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(discordUrl)
            && string.Equals(websiteUrl.Trim(), discordUrl.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return System.Windows.Visibility.Collapsed;
        }

        return System.Windows.Visibility.Visible;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a boolean IsOnline to an up/down arrow symbol.
/// </summary>
public class OnlineStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "\u25B2" : "\u25BC"; // ▲ or ▼

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a boolean IsOnline to green (up) or red (down) brush.
/// </summary>
public class OnlineStatusColorConverter : IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush Green
        = new(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly System.Windows.Media.SolidColorBrush Red
        = new(System.Windows.Media.Color.FromRgb(0xE5, 0x39, 0x35));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Green : Red;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts GameSessionStatus to a friendly display string.
/// </summary>
public class SessionStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            ShadowLauncher.Core.Models.GameSessionStatus.Launching => "Launching",
            ShadowLauncher.Core.Models.GameSessionStatus.LoginScreen => "Login Screen",
            ShadowLauncher.Core.Models.GameSessionStatus.CharacterSelection => "Character Select",
            ShadowLauncher.Core.Models.GameSessionStatus.InGame => "Alive",
            ShadowLauncher.Core.Models.GameSessionStatus.Exiting => "Dead",
            ShadowLauncher.Core.Models.GameSessionStatus.Offline => "Dead",
            _ => "Unknown"
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → Visibility (true = Visible, false = Collapsed).</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → Visibility (true = Collapsed, false = Visible).</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
