using System.Windows.Input;

namespace ShadowLauncher.Presentation.ViewModels;

public class AddProfileViewModel : ViewModelBase
{
    private readonly IReadOnlyCollection<string> _existingNames;
    private string _profileName = string.Empty;
    private string _errorText = string.Empty;

    public AddProfileViewModel(IReadOnlyCollection<string> existingNames)
    {
        _existingNames = existingNames;
        SaveCommand = new RelayCommand(Save, () => CanSave);
    }

    public event EventHandler? SaveCompleted;

    public string ProfileName
    {
        get => _profileName;
        set
        {
            if (SetProperty(ref _profileName, value))
            {
                OnPropertyChanged(nameof(CanSave));
                ErrorText = string.Empty;
            }
        }
    }

    public string ErrorText
    {
        get => _errorText;
        private set => SetProperty(ref _errorText, value);
    }

    public bool CanSave => !string.IsNullOrWhiteSpace(ProfileName);

    public ICommand SaveCommand { get; }

    private void Save()
    {
        if (!CanSave) return;
        if (_existingNames.Any(n => n.Equals(ProfileName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText = "Profile names must be unique.";
            return;
        }
        SaveCompleted?.Invoke(this, EventArgs.Empty);
    }
}
