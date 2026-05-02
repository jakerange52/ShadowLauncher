using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.Accounts;

public interface IAccountService
{
    event EventHandler? AccountsChanged;

    Task<Account?> GetAccountAsync(string accountId);
    Task<IEnumerable<Account>> GetAllAccountsAsync();
    Task<Account> CreateAccountAsync(string name, string password);
    Task UpdateAccountAsync(Account account);
    Task DeleteAccountAsync(string accountId);
}
