using System.Windows;
using ShadowLauncher.Presentation.ViewModels;

namespace ShadowLauncher.Presentation.Views;

public partial class EditProfilesWindow : Window
{
    public EditProfilesWindow(EditProfilesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.SaveCompleted += (_, _) => { DialogResult = true; };
        Loaded += (_, _) => OffsetFromOwner();
        Closed += (_, _) => WindowPositionHelper.Save(this);
    }

    private void OffsetFromOwner()
    {
        if (Owner is null) return;
        WindowPositionHelper.RestoreOrOffset(this, Owner);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
