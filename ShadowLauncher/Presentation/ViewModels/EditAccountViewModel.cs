using System.Windows.Input;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Presentation.ViewModels;

public class EditAccountViewModel : ViewModelBase
{
    private string _notes;
    private string _preferencePath;
    private string _errorText = string.Empty;

    public EditAccountViewModel(Account account)
    {
        Account = account;
        _notes = account.Notes;
        _preferencePath = account.PreferencePath;

        SaveCommand = new RelayCommand(Save);
        BrowsePreferencePathCommand = new RelayCommand(() => BrowseRequested?.Invoke(this, nameof(PreferencePath)));
    }

    public Account Account { get; }

    public string AccountName => Account.Name;

    public event EventHandler? SaveCompleted;
    public event EventHandler<string>? BrowseRequested;

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public string PreferencePath
    {
        get => _preferencePath;
        set => SetProperty(ref _preferencePath, value);
    }

    public string ErrorText
    {
        get => _errorText;
        set => SetProperty(ref _errorText, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand BrowsePreferencePathCommand { get; }

    private void Save()
    {
        Account.Notes = Notes;
        Account.PreferencePath = PreferencePath.Trim();
        ErrorText = string.Empty;
        SaveCompleted?.Invoke(this, EventArgs.Empty);
    }
}
