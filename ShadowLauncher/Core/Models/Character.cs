namespace ShadowLauncher.Core.Models;

public class Character : IEquatable<Character>
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public int Level { get; set; }
    public bool IsDefault { get; set; }
    public DateTime LastPlayedDate { get; set; }
    public string Class { get; set; } = string.Empty;
    public string Race { get; set; } = string.Empty;
    public Dictionary<string, string> Settings { get; set; } = [];
    public long TotalPlaytimeSeconds { get; set; }

    public bool IsValid()
        => !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(AccountId)
        && Level >= 1 && Level <= 275;

    public void RecordPlaySession(TimeSpan duration)
    {
        LastPlayedDate = DateTime.UtcNow;
        TotalPlaytimeSeconds += (long)duration.TotalSeconds;
    }

    public bool Equals(Character? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as Character);
    public override int GetHashCode() => Id?.GetHashCode() ?? 0;
}
