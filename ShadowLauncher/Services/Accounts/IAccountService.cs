using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.Accounts;

public interface IAccountService
{
    event EventHandler? AccountsChanged;

    Task<IEnumerable<Account>> GetAllAccountsAsync();
    Task<Account> CreateAccountAsync(string name, string password, string? preferencePath = null);
    Task UpdateAccountAsync(Account account);
    Task DeleteAccountAsync(string accountId);
}
