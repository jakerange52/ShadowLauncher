using System.Diagnostics;
using System.Windows;

namespace ShadowLauncher.Presentation.Views;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => OffsetFromOwner();
    }

    private void OffsetFromOwner()
    {
        if (Owner is null) return;
        AddAccountWindow.ClampedOffset(this, Owner);
    }

    private void OpenPreferences_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Asheron's Call", "UserPreferences.ini");

        if (File.Exists(path))
        {
            Process.Start("notepad.exe", path);
        }
        else
        {
            // Try without the apostrophe variant
            var altPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Asherons Call", "UserPreferences.ini");

            if (File.Exists(altPath))
            {
                Process.Start("notepad.exe", altPath);
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
