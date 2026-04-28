using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.Accounts;

public class AccountService : IAccountService
{
    private readonly IRepository<Account> _repository;
    private readonly ILogger<AccountService> _logger;

    public AccountService(IRepository<Account> repository, ILogger<AccountService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public Task<Account?> GetAccountAsync(string accountId)
        => _repository.GetByIdAsync(accountId);

    public Task<IEnumerable<Account>> GetAllAccountsAsync()
        => _repository.GetAllAsync();

    public async Task<Account> CreateAccountAsync(string name, string password)
    {
        var existing = await _repository.FindAsync(a =>
            a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing.Any())
            throw new InvalidOperationException($"Account '{name}' already exists.");

        var account = new Account
        {
            Id = name.ToLowerInvariant(),
            Name = name,
            PasswordHash = password,
            CreatedDate = DateTime.UtcNow,
            IsActive = true
        };

        await _repository.AddAsync(account);
        _logger.LogInformation("Account created: {Name}", name);
        return account;
    }

    public Task UpdateAccountAsync(Account account)
        => _repository.UpdateAsync(account);

    public async Task DeleteAccountAsync(string accountId)
    {
        await _repository.DeleteAsync(accountId);
        _logger.LogInformation("Account deleted: {Id}", accountId);
    }
}
