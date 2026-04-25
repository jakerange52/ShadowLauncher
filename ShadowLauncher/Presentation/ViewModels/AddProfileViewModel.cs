using System.Windows.Input;

namespace ShadowLauncher.Presentation.ViewModels;

public class AddProfileViewModel : ViewModelBase
{
    private string _profileName = string.Empty;

    public AddProfileViewModel()
    {
        SaveCommand = new RelayCommand(Save, () => CanSave);
    }

    public event EventHandler? SaveCompleted;

    public string ProfileName
    {
        get => _profileName;
        set
        {
            if (SetProperty(ref _profileName, value))
                OnPropertyChanged(nameof(CanSave));
        }
    }

    public bool CanSave => !string.IsNullOrWhiteSpace(ProfileName);

    public ICommand SaveCommand { get; }

    private void Save()
    {
        if (!CanSave) return;
        SaveCompleted?.Invoke(this, EventArgs.Empty);
    }
}
