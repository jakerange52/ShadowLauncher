using System.Collections.ObjectModel;
using System.Windows.Input;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Services.Profiles;

namespace ShadowLauncher.Presentation.ViewModels;

/// <summary>Wraps a profile for editing in the EditProfiles window.</summary>
public class EditableProfile : ViewModelBase
{
    private string _name;

    /// <summary>Existing profile being edited.</summary>
    public EditableProfile(LaunchProfile source)
    {
        Id = source.Id;
        _name = source.Name;
        IsNew = false;
    }

    /// <summary>Brand-new profile not yet persisted.</summary>
    public EditableProfile()
    {
        Id = $"__new__{Guid.NewGuid()}";
        _name = string.Empty;
        IsNew = true;
    }

    public string Id { get; private set; }
    public bool IsNew { get; private set; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>Called after the profile is created so the entry tracks its real ID.</summary>
    public void CommitId(string realId)
    {
        Id = realId;
        IsNew = false;
    }
}

public class EditProfilesViewModel : ViewModelBase
{
    private readonly ProfileService _profileService;

    public EditProfilesViewModel(ProfileService profileService)
    {
        _profileService = profileService;

        foreach (var p in profileService.Profiles)
            Profiles.Add(new EditableProfile(p));

        SaveCommand = new RelayCommand(Save);
        DeleteCommand = new RelayCommand<string>(Delete);
        AddProfileCommand = new RelayCommand(AddProfile);
    }

    public event EventHandler? SaveCompleted;

    public ObservableCollection<EditableProfile> Profiles { get; } = [];

    private string _errorText = string.Empty;
    public string ErrorText
    {
        get => _errorText;
        private set => SetProperty(ref _errorText, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand AddProfileCommand { get; }

    private void Save()
    {
        var names = Profiles
            .Select(p => p.Name.Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        if (names.Count != names.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            ErrorText = "Profile names must be unique.";
            return;
        }

        ErrorText = string.Empty;

        foreach (var ep in Profiles)
        {
            if (string.IsNullOrWhiteSpace(ep.Name)) continue;
            if (ep.IsNew)
                ep.CommitId(_profileService.CreateProfile(ep.Name.Trim()).Id);
            else
                _profileService.RenameProfile(ep.Id, ep.Name.Trim());
        }
        SaveCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void Delete(string? id)
    {
        if (id is null) return;
        if (Profiles.Count <= 1) return;
        var entry = Profiles.FirstOrDefault(p => p.Id == id);
        if (entry is null) return;
        if (!entry.IsNew)
            _profileService.DeleteProfile(id);
        Profiles.Remove(entry);
    }

    private void AddProfile()
    {
        Profiles.Add(new EditableProfile());
    }
}
