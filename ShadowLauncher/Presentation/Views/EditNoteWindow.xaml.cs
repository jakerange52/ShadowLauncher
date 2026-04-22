using System.Windows;

namespace ShadowLauncher.Presentation.Views;

public partial class EditNoteWindow : Window
{
    public string NoteText { get; private set; }

    public EditNoteWindow(string accountName, string currentNote)
    {
        InitializeComponent();
        TitleText.Text = $"Note for '{accountName}'";
        NoteText = currentNote;
        NoteTextBox.Text = currentNote;
        NoteTextBox.Focus();
        NoteTextBox.SelectAll();

        Loaded += (_, _) => OffsetFromOwner();
    }

    private void OffsetFromOwner()
    {
        if (Owner is null) return;
        AddAccountWindow.ClampedOffset(this, Owner);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        NoteText = NoteTextBox.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
