using System.Windows;
using System.Windows.Controls;
using ShadowLauncher.Presentation.ViewModels;

namespace ShadowLauncher.Presentation.Views;

public partial class AddAccountWindow : Window
{
    public AddAccountWindow(AddAccountViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // PasswordBox can't be data-bound, so sync manually
        PasswordBox.PasswordChanged += (_, _) => viewModel.Password = PasswordBox.Password;
        viewModel.SaveCompleted += (_, _) => DialogResult = true;

        Loaded += (_, _) => OffsetFromOwner();
        Closed += (_, _) => WindowPositionHelper.Save(this);
    }

    private void OffsetFromOwner()
    {
        if (Owner is null) return;
        WindowPositionHelper.RestoreOrOffset(this, Owner);
    }

    internal static void ClampedOffset(Window child, Window owner)
    {
        var vLeft   = SystemParameters.VirtualScreenLeft;
        var vTop    = SystemParameters.VirtualScreenTop;
        var vRight  = vLeft + SystemParameters.VirtualScreenWidth;
        var vBottom = vTop  + SystemParameters.VirtualScreenHeight;

        // Center the child over the owner window
        double desiredLeft = owner.Left + (owner.Width  - child.Width)  / 2;
        double desiredTop  = owner.Top  + (owner.Height - child.Height) / 2;

        child.Left = Math.Max(vLeft, Math.Min(desiredLeft, vRight  - child.Width));
        child.Top  = Math.Max(vTop,  Math.Min(desiredTop,  vBottom - child.Height));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
