using System.Windows.Input;

namespace ShadowLauncher.Presentation.ViewModels;

public class AddAccountViewModel : ViewModelBase
{
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _errorText = string.Empty;

    public AddAccountViewModel()
    {
        SaveCommand = new RelayCommand(Save, () => CanSave);
    }

    public event EventHandler? SaveCompleted;

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

    public string ErrorText
    {
        get => _errorText;
        set => SetProperty(ref _errorText, value);
    }

    public bool CanSave => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

    public ICommand SaveCommand { get; }

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
