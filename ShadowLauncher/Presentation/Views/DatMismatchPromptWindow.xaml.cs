using System.Windows;

namespace ShadowLauncher.Presentation.Views;

public partial class DatMismatchPromptWindow : Window
{
    public bool RefreshRequested { get; private set; }

    public DatMismatchPromptWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
    }

    public static bool PromptRefresh(Window owner)
    {
        var w = new DatMismatchPromptWindow(owner);
        w.ShowDialog();
        return w.RefreshRequested;
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        RefreshRequested = true;
        DialogResult = true;
        Close();
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        RefreshRequested = false;
        DialogResult = false;
        Close();
    }
}
