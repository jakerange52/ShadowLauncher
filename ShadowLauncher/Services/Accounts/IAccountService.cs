using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.Accounts;

public interface IAccountService
{
    Task<Account?> GetAccountAsync(string accountId);
    Task<IEnumerable<Account>> GetAllAccountsAsync();
    Task<Account> CreateAccountAsync(string name, string password, string? email = null);
    Task UpdateAccountAsync(Account account);
    Task DeleteAccountAsync(string accountId);
}
