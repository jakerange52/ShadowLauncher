using System.IO;
using System.Windows;
using Microsoft.Win32;
using ShadowLauncher.Presentation.ViewModels;

namespace ShadowLauncher.Presentation.Views;

public partial class EditAccountWindow : Window
{
    public EditAccountWindow(EditAccountViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.SaveCompleted += (_, _) => DialogResult = true;
        viewModel.BrowseRequested += (_, _) => BrowsePreferencePath(viewModel);

        Loaded += (_, _) => OffsetFromOwner();
        Closed += (_, _) => WindowPositionHelper.Save(this);
    }

    private void BrowsePreferencePath(EditAccountViewModel viewModel)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "INI files|UserPreferences.ini;*.ini|All files|*.*",
            Title = "Select UserPreferences.ini for this account"
        };

        if (!string.IsNullOrWhiteSpace(viewModel.PreferencePath) && File.Exists(viewModel.PreferencePath))
            dialog.InitialDirectory = Path.GetDirectoryName(viewModel.PreferencePath);

        if (dialog.ShowDialog(this) == true)
            viewModel.PreferencePath = dialog.FileName;
    }

    private void OffsetFromOwner()
    {
        if (Owner is null) return;
        WindowPositionHelper.RestoreOrOffset(this, Owner);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
