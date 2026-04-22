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
    }

    private void OffsetFromOwner()
    {
        if (Owner is null) return;
        ClampedOffset(this, Owner);
    }

    internal static void ClampedOffset(Window child, Window owner)
    {
        var vLeft   = SystemParameters.VirtualScreenLeft;
        var vTop    = SystemParameters.VirtualScreenTop;
        var vRight  = vLeft + SystemParameters.VirtualScreenWidth;
        var vBottom = vTop  + SystemParameters.VirtualScreenHeight;

        double desiredLeft = owner.Left + owner.Width + 12;
        if (desiredLeft + child.Width > vRight)
            desiredLeft = owner.Left - child.Width - 12;

        child.Left = Math.Max(vLeft, Math.Min(desiredLeft, vRight  - child.Width));
        child.Top  = Math.Max(vTop,  Math.Min(owner.Top + (owner.Height - child.Height) / 2, vBottom - child.Height));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
