using System.Windows;
using System.Windows.Controls;
using ShadowLauncher.Presentation.ViewModels;

namespace ShadowLauncher.Presentation.Views;

public partial class AddProfileWindow : Window
{
    public AddProfileWindow(AddProfileViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.SaveCompleted += (_, _) => { DialogResult = true; };
        Loaded += (_, _) => ProfileNameBox.Focus();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
