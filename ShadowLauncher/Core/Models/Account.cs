namespace ShadowLauncher.Core.Models;

public class Account : IEquatable<Account>
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public List<Character> Characters { get; set; } = [];
    public List<string> ServerIds { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedDate { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string DefaultCharacter { get; set; } = string.Empty;
    public int LaunchCount { get; set; }

    public bool IsValid()
        => !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(PasswordHash);

    public Character? GetCharacter(string characterName)
        => Characters?.FirstOrDefault(c =>
            c.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));

    public bool HasCharacter(string characterName) => GetCharacter(characterName) is not null;
    public bool HasServer(string serverId) => ServerIds?.Contains(serverId) ?? false;

    public bool Equals(Account? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as Account);
    public override int GetHashCode() => Id?.GetHashCode() ?? 0;
}
