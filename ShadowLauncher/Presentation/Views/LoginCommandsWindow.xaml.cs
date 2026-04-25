using System.Windows;
using ShadowLauncher.Services.LoginCommands;

namespace ShadowLauncher.Presentation.Views;

public partial class LoginCommandsWindow : Window
{
    private readonly LoginCommandsService _service;

    public LoginCommandsWindow(LoginCommandsService service)
    {
        InitializeComponent();
        _service = service;

        CommandsTextBox.Text = _service.GetGlobalCommands();
        WaitMsTextBox.Text = "3000";

        Loaded += (_, _) => OffsetFromOwner();
        Closed += (_, _) => WindowPositionHelper.Save(this);
    }

    private void OffsetFromOwner()
    {
        if (Owner is null) return;
        WindowPositionHelper.RestoreOrOffset(this, Owner);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        int.TryParse(WaitMsTextBox.Text, out var waitMs);
        if (waitMs <= 0) waitMs = 3000;
        _service.SetGlobalCommands(CommandsTextBox.Text, waitMs);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
