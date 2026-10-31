using System.Text;
using System.Windows;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Presentation.Views;

public partial class DatReleaseRefreshPromptWindow : Window
{
    public bool RefreshRequested { get; private set; }

    public DatReleaseRefreshPromptWindow(Window owner, IReadOnlyList<DatReleaseCheckResult> stale)
    {
        Owner = owner;
        InitializeComponent();
        DetailsText.Text = BuildDetails(stale);
    }

    public static bool PromptRefresh(Window owner, IReadOnlyList<DatReleaseCheckResult> stale)
    {
        var dialog = new DatReleaseRefreshPromptWindow(owner, stale);
        dialog.ShowDialog();
        return dialog.RefreshRequested;
    }

    private static string BuildDetails(IReadOnlyList<DatReleaseCheckResult> stale)
    {
        var sb = new StringBuilder();
        foreach (var item in stale)
        {
            if (sb.Length > 0)
                sb.AppendLine();

            sb.Append(item.ServerName);
            sb.AppendLine();
            sb.Append("  Cached: ");
            sb.Append(item.CachedTag);
            sb.AppendLine();
            sb.Append("  Latest: ");
            sb.Append(item.RemoteTag);
        }

        return sb.ToString();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshRequested = true;
        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
