using System.Windows;
using Microsoft.Win32;
using ShadowLauncher.Presentation.ViewModels;

namespace ShadowLauncher.Presentation.Views;

public partial class AddServerWindow : Window
{
    public AddServerWindow(AddServerViewModel viewModel, string title = "Add Server")
    {
        InitializeComponent();  
        Title = title;
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

    private void BrowseCustomDatRegistry_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Custom Dat Registry Folder"
        };

        if (dialog.ShowDialog(this) == true)
        {
            var vm = (AddServerViewModel)DataContext;
            vm.CustomDatRegistryPath = dialog.FolderName;
        }
    }
}
