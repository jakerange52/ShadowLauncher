using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.Accounts;

public interface IAccountService
{
    Task<Account?> GetAccountAsync(string accountId);
    Task<IEnumerable<Account>> GetAllAccountsAsync();
    Task<Account?> GetAccountByNameAsync(string accountName);
    Task<bool> AccountExistsAsync(string accountName);
    Task<Account> CreateAccountAsync(string name, string password, string? email = null);
    Task UpdateAccountAsync(Account account);
    Task DeleteAccountAsync(string accountId);
    Task<IEnumerable<Character>> GetCharactersAsync(string accountId);
    Task AddCharacterAsync(string accountId, Character character);
    Task RemoveCharacterAsync(string accountId, string characterName);
}
