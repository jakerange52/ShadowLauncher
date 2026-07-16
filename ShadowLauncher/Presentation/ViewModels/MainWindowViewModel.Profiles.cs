using ShadowLauncher.Core.Models;
using ShadowLauncher.Services.Profiles;
using ShadowLauncher.Presentation.Views;

namespace ShadowLauncher.Presentation.ViewModels;

public partial class MainWindowViewModel
{
    private void AddProfile()
    {
        var vm = new AddProfileViewModel(Profiles.Select(p => p.Name).ToList());
        var window = new AddProfileWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        if (window.ShowDialog() == true)
        {
            var profile = _profileService.CreateProfile(vm.ProfileName);
            Profiles.Add(profile);
            CurrentProfile = profile;
        }
    }

    private void ApplyProfile(LaunchProfile profile)
    {
        _applyingProfile = true;
        try
        {
            _profileService.SetActiveProfile(profile.Id);
            _config.KillOnMissingHeartbeat = profile.KillOnMissingHeartbeat;
            _config.KillHeartbeatTimeoutSeconds = profile.KillHeartbeatTimeoutSeconds;
            _config.AutoRelaunch = profile.AutoRelaunch;
            _config.AutoRelaunchDelaySeconds = profile.AutoRelaunchDelaySeconds;
            _config.Save();
            OnPropertyChanged(nameof(KillOnMissingHeartbeat));
            OnPropertyChanged(nameof(KillHeartbeatTimeoutSeconds));
            OnPropertyChanged(nameof(AutoRelaunch));
            OnPropertyChanged(nameof(AutoRelaunchDelaySeconds));
            ProfileSelectionRestoreRequested?.Invoke(profile.SelectedAccountIds, profile.SelectedServerIds);
            _loginCommandsService.ApplyFromProfile(profile);
        }
        finally
        {
            _applyingProfile = false;
        }
    }

    private void SaveCurrentProfile()
    {
        if (_currentProfile is null || _applyingProfile) return;
        _currentProfile.SelectedAccountIds.Clear();
        _currentProfile.SelectedAccountIds.AddRange(SelectedAccounts.Select(a => a.Id));
        _currentProfile.SelectedServerIds.Clear();
        _currentProfile.SelectedServerIds.AddRange(SelectedServers.Select(s => s.Id));
        _currentProfile.KillOnMissingHeartbeat = _config.KillOnMissingHeartbeat;
        _currentProfile.KillHeartbeatTimeoutSeconds = _config.KillHeartbeatTimeoutSeconds;
        _currentProfile.AutoRelaunch = _config.AutoRelaunch;
        _currentProfile.AutoRelaunchDelaySeconds = _config.AutoRelaunchDelaySeconds;
        _loginCommandsService.SnapshotIntoProfile(_currentProfile);
        _profileService.SaveProfile(_currentProfile);
    }

    private void RefreshProfiles()
    {
        var previousId = _currentProfile?.Id;
        Profiles.Clear();
        foreach (var p in _profileService.Profiles)
            Profiles.Add(p);

        var match = Profiles.FirstOrDefault(p => p.Id == previousId);
        if (match is not null)
        {
            _currentProfile = match;
            OnPropertyChanged(nameof(CurrentProfile));
        }
        else
        {
            CurrentProfile = _profileService.ActiveProfile;
        }
    }
}
