using System.Windows;
using ShadowLauncher.Presentation.ViewModels;

namespace ShadowLauncher.Presentation.Views;

public partial class BrowseServersWindow : Window
{
    public BrowseServersWindow(BrowseServersViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            OffsetFromOwner();
            await viewModel.LoadAsync();
        };
    }

    private void OffsetFromOwner()
    {
        if (Owner is null) return;
        AddAccountWindow.ClampedOffset(this, Owner);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
