using System.Windows;

namespace ShadowLauncher.Presentation.Views;

public partial class DatVersionCheckingWindow : Window
{
    public DatVersionCheckingWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
    }
}
