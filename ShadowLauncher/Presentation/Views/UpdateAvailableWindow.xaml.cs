using System.Diagnostics;
using System.Windows;

namespace ShadowLauncher.Presentation.Views;

public partial class UpdateAvailableWindow : Window
{
    private readonly string _releaseUrl;

    public UpdateAvailableWindow(Version? currentVersion, Version? remoteVersion, string releaseUrl)
    {
        InitializeComponent();
        _releaseUrl = releaseUrl;
        SummaryText.Text =
            $"ShadowLauncher v{FormatVersion(remoteVersion)} is available (you have v{FormatVersion(currentVersion)}).";

        if (string.IsNullOrWhiteSpace(releaseUrl))
            NotesText.Visibility = Visibility.Collapsed;

        Loaded += (_, _) => OffsetFromOwner();
    }

    public static bool PromptInstall(Version? currentVersion, Version? remoteVersion, string releaseUrl, Window? owner = null)
    {
        owner ??= System.Windows.Application.Current.MainWindow;
        var dialog = new UpdateAvailableWindow(currentVersion, remoteVersion, releaseUrl)
        {
            Owner = owner?.IsLoaded == true ? owner : null
        };
        return dialog.ShowDialog() == true;
    }

    private static string FormatVersion(Version? version) =>
        version is null ? "?" : $"{version.Major}.{version.Minor}.{version.Build}";

    private void OffsetFromOwner()
    {
        if (Owner is null) return;
        AddAccountWindow.ClampedOffset(this, Owner);
    }

    private void NotesLink_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_releaseUrl))
            return;

        Process.Start(new ProcessStartInfo(_releaseUrl) { UseShellExecute = true });
    }

    private void Install_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
