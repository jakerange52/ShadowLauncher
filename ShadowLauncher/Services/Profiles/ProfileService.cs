using System.Text.Json;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.Profiles;

public class ProfileService
{
    private readonly string _filePath;
    private List<LaunchProfile> _profiles = [];
    private string _activeProfileId = string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ProfileService(string filePath)
    {
        _filePath = filePath;
        Load();
        if (_profiles.Count == 0)
        {
            var defaultProfile = new LaunchProfile { Id = Guid.NewGuid().ToString(), Name = "Default" };
            _profiles.Add(defaultProfile);
            _activeProfileId = defaultProfile.Id;
            Save();
        }
        else if (!_profiles.Any(p => p.Id == _activeProfileId))
        {
            _activeProfileId = _profiles[0].Id;
            SaveMeta();
        }
    }

    public IReadOnlyList<LaunchProfile> Profiles => _profiles.AsReadOnly();

    public LaunchProfile? ActiveProfile => _profiles.FirstOrDefault(p => p.Id == _activeProfileId);

    public LaunchProfile SetActiveProfile(string id)
    {
        var profile = _profiles.First(p => p.Id == id);
        _activeProfileId = id;
        SaveMeta();
        return profile;
    }

    public LaunchProfile CreateProfile(string name)
    {
        var profile = new LaunchProfile { Id = Guid.NewGuid().ToString(), Name = name };
        _profiles.Add(profile);
        Save();
        return profile;
    }

    public void SaveProfile(LaunchProfile profile)
    {
        var index = _profiles.FindIndex(p => p.Id == profile.Id);
        if (index >= 0)
            _profiles[index] = profile;
        Save();
    }

    public void RenameProfile(string id, string newName)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null) return;
        profile.Name = newName;
        Save();
    }

    public bool DeleteProfile(string id)
    {
        if (_profiles.Count <= 1) return false; // always keep at least one
        var removed = _profiles.RemoveAll(p => p.Id == id) > 0;
        if (removed && _activeProfileId == id)
            _activeProfileId = _profiles[0].Id;
        if (removed) Save();
        return removed;
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var dto = JsonSerializer.Deserialize<ProfileStore>(File.ReadAllText(_filePath), JsonOptions);
            if (dto is not null)
            {
                _profiles = dto.Profiles ?? [];
                _activeProfileId = dto.ActiveProfileId ?? string.Empty;
            }
        }
        catch { /* corrupt file — start fresh */ }
    }

    private void Save()
    {
        var dto = new ProfileStore { Profiles = _profiles, ActiveProfileId = _activeProfileId };
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(dto, JsonOptions));
    }

    private void SaveMeta()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var dto = JsonSerializer.Deserialize<ProfileStore>(File.ReadAllText(_filePath), JsonOptions);
            if (dto is not null)
            {
                dto.ActiveProfileId = _activeProfileId;
                File.WriteAllText(_filePath, JsonSerializer.Serialize(dto, JsonOptions));
            }
        }
        catch { Save(); }
    }

    private sealed class ProfileStore
    {
        public List<LaunchProfile> Profiles { get; set; } = [];
        public string ActiveProfileId { get; set; } = string.Empty;
    }
}
