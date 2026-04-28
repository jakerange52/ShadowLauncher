using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.Accounts;

public class AccountService : IAccountService
{
    private readonly IRepository<Account> _repository;
    private readonly IEventAggregator _events;
    private readonly ILogger<AccountService> _logger;

    public AccountService(IRepository<Account> repository, IEventAggregator events, ILogger<AccountService> logger)
    {
        _repository = repository;
        _events = events;
        _logger = logger;
    }

    public Task<Account?> GetAccountAsync(string accountId)
        => _repository.GetByIdAsync(accountId);

    public Task<IEnumerable<Account>> GetAllAccountsAsync()
        => _repository.GetAllAsync();

    public async Task<Account?> GetAccountByNameAsync(string accountName)
    {
        var accounts = await _repository.FindAsync(a =>
            a.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase));
        return accounts.FirstOrDefault();
    }

    public async Task<bool> AccountExistsAsync(string accountName)
        => await GetAccountByNameAsync(accountName) is not null;

    public async Task<Account> CreateAccountAsync(string name, string password, string? email = null)
    {
        if (await AccountExistsAsync(name))
            throw new InvalidOperationException($"Account '{name}' already exists.");

        var account = new Account
        {
            Id = name.ToLowerInvariant(),
            Name = name,
            PasswordHash = password, // stored as-is for game login
            Email = email ?? string.Empty,
            CreatedDate = DateTime.UtcNow,
            IsActive = true
        };

        await _repository.AddAsync(account);
        _logger.LogInformation("Account created: {Name}", name);
        _events.Publish(new AccountCreatedEvent(account));
        return account;
    }

    public async Task UpdateAccountAsync(Account account)
    {
        await _repository.UpdateAsync(account);
        _events.Publish(new AccountUpdatedEvent(account));
    }

    public async Task DeleteAccountAsync(string accountId)
    {
        await _repository.DeleteAsync(accountId);
        _logger.LogInformation("Account deleted: {Id}", accountId);
        _events.Publish(new AccountDeletedEvent(accountId));
    }

    public async Task<IEnumerable<Character>> GetCharactersAsync(string accountId)
    {
        var account = await _repository.GetByIdAsync(accountId);
        return account?.Characters ?? [];
    }

    public async Task AddCharacterAsync(string accountId, Character character)
    {
        var account = await _repository.GetByIdAsync(accountId)
            ?? throw new InvalidOperationException($"Account '{accountId}' not found.");
        character.AccountId = accountId;
        character.Id = Guid.NewGuid().ToString();
        account.Characters.Add(character);
        await _repository.UpdateAsync(account);
    }

    public async Task RemoveCharacterAsync(string accountId, string characterName)
    {
        var account = await _repository.GetByIdAsync(accountId)
            ?? throw new InvalidOperationException($"Account '{accountId}' not found.");
        account.Characters.RemoveAll(c =>
            c.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase));
        await _repository.UpdateAsync(account);
    }
}

public record AccountCreatedEvent(Account Account);
public record AccountUpdatedEvent(Account Account);
public record AccountDeletedEvent(string AccountId);
