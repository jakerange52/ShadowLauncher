using System.Windows;
using ShadowLauncher.Presentation.ViewModels;

namespace ShadowLauncher.Presentation.Views;

public partial class AddServerWindow : Window
{
    public AddServerWindow(AddServerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.SaveCompleted += (_, _) => DialogResult = true;

        Loaded += (_, _) => OffsetFromOwner();
    }

    private void OffsetFromOwner()
    {
        if (Owner is null) return;
        AddAccountWindow.ClampedOffset(this, Owner);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
