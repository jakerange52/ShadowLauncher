using System.Windows;

namespace ShadowLauncher.Presentation.Views;

public partial class CharacterLoginCommandsEditorWindow : Window
{
    public string Commands { get; private set; } = string.Empty;
    public int WaitMs { get; private set; } = 3000;

    public CharacterLoginCommandsEditorWindow(string accountName, string serverName, string characterName, string currentCommands)
    {
        InitializeComponent();
        TitleText.Text = $"{accountName} / {serverName} / {characterName}";
        CommandsTextBox.Text = currentCommands;
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
        Commands = CommandsTextBox.Text;
        int.TryParse(WaitMsTextBox.Text, out var waitMs);
        WaitMs = waitMs > 0 ? waitMs : 3000;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
