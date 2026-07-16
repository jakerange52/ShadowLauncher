using System.Windows.Input;

namespace ShadowLauncher.Presentation.ViewModels;

public class AddAccountViewModel : ViewModelBase
{
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _preferencePath = string.Empty;
    private string _errorText = string.Empty;

    public AddAccountViewModel()
    {
        SaveCommand = new RelayCommand(Save, () => CanSave);
        BrowsePreferencePathCommand = new RelayCommand(() => BrowseRequested?.Invoke(this, nameof(PreferencePath)));
    }

    public event EventHandler? SaveCompleted;
    public event EventHandler<string>? BrowseRequested;

    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
                OnPropertyChanged(nameof(CanSave));
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
                OnPropertyChanged(nameof(CanSave));
        }
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

    public bool CanSave => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

    public ICommand SaveCommand { get; }
    public ICommand BrowsePreferencePathCommand { get; }

    private void Save()
    {
        if (!CanSave)
        {
            ErrorText = "Username and password are required.";
            return;
        }

        ErrorText = string.Empty;
        SaveCompleted?.Invoke(this, EventArgs.Empty);
    }
}
