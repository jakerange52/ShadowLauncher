using System.Diagnostics;
using System.Windows;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Presentation.ViewModels;
using ShadowLauncher.Presentation.Views;

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

    private void EditServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Server server && server.IsManuallyAdded)
        {
            var vm = new AddServerViewModel(_config);
            vm.LoadFromServer(server);
            var editWindow = new AddServerWindow(vm, "Edit Server") { Owner = this };
            if (editWindow.ShowDialog() == true)
                _ = _viewModel.UpdateServerAsync(vm.ApplyToServer(server));
        }
    }
}
