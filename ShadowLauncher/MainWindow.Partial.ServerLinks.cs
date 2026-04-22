using System.Diagnostics;
using System.Windows;

namespace ShadowLauncher;

public partial class MainWindow : Window
{
    // ...existing code...
    private void OpenDiscord_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ShadowLauncher.Core.Models.Server server && !string.IsNullOrWhiteSpace(server.DiscordUrl))
        {
            Process.Start(new ProcessStartInfo(server.DiscordUrl) { UseShellExecute = true });
        }
    }

    private void OpenWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ShadowLauncher.Core.Models.Server server && !string.IsNullOrWhiteSpace(server.WebsiteUrl))
        {
            Process.Start(new ProcessStartInfo(server.WebsiteUrl) { UseShellExecute = true });
        }
    }
}
