namespace ShadowLauncher.Core.Models;

public class Account : IEquatable<Account>
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    public bool Equals(Account? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as Account);
    public override int GetHashCode() => Id.GetHashCode();
}
