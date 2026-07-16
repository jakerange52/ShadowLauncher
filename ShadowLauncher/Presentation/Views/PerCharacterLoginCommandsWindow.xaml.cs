using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ShadowLauncher.Infrastructure.Paths;
using ShadowLauncher.Services.Accounts;
using ShadowLauncher.Services.LoginCommands;
using ShadowLauncher.Services.Servers;

namespace ShadowLauncher.Presentation.Views;

public partial class PerCharacterLoginCommandsWindow : Window
{
    private readonly LoginCommandsService _loginService;
    private readonly IAccountService _accountService;
    private readonly IServerService _serverService;
    private readonly ObservableCollection<CharacterCommandEntry> _entries = [];
    private readonly CollectionViewSource _view = new();
    private FileSystemWatcher? _charWatcher;

    public PerCharacterLoginCommandsWindow(
        LoginCommandsService loginService,
        IAccountService accountService,
        IServerService serverService)
    {
        InitializeComponent();
        _loginService = loginService;
        _accountService = accountService;
        _serverService = serverService;
        _view.Source = _entries;
        _view.Filter += (_, e) =>
        {
            var q = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(q)) { e.Accepted = true; return; }
            if (e.Item is CharacterCommandEntry entry)
                e.Accepted = entry.AvailableCharacters.Any(c => c.Contains(q, StringComparison.OrdinalIgnoreCase))
                          || entry.AccountName.Contains(q, StringComparison.OrdinalIgnoreCase)
                          || entry.ServerName.Contains(q, StringComparison.OrdinalIgnoreCase);
        };
        CharacterGrid.ItemsSource = _view.View;

        Loaded += async (_, _) =>
        {
            OffsetFromOwner();
            await RefreshEntriesAsync();
            StartWatchingCharacterFiles();
        };

        Closed += (_, _) => { StopWatchingCharacterFiles(); WindowPositionHelper.Save(this); };
    }

    private void StartWatchingCharacterFiles()
    {
        var charFolder = ShadowLauncherPaths.CharactersFolder;

        if (!Directory.Exists(charFolder))
            Directory.CreateDirectory(charFolder);

        _charWatcher = new FileSystemWatcher(charFolder, "characters_*.txt")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _charWatcher.Changed += OnCharacterFileChanged;
        _charWatcher.Created += OnCharacterFileChanged;
    }

    private void StopWatchingCharacterFiles()
    {
        if (_charWatcher is not null)
        {
            _charWatcher.EnableRaisingEvents = false;
            _charWatcher.Dispose();
            _charWatcher = null;
        }
    }

    private void OnCharacterFileChanged(object sender, FileSystemEventArgs e)
    {
        // Delay slightly to let the write finish, then refresh on UI thread
        Thread.Sleep(200);
        Dispatcher.InvokeAsync(async () => await RefreshEntriesAsync());
    }

    private async Task RefreshEntriesAsync()
    {
        _entries.Clear();

        var accounts = await _accountService.GetAllAccountsAsync();
        var servers = await _serverService.GetAllServersAsync();

        foreach (var account in accounts)
        {
            foreach (var server in servers)
            {
                // Get known characters from ShadowFilter's character files
                var knownChars = _loginService.GetKnownCharacters(server.Name, account.Name);

                // Build the available list: "any" first, then known characters
                var available = new List<string> { "any" };
                available.AddRange(knownChars.OrderBy(n => n));

                // Load saved default character selection
                var savedChar = _loginService.GetDefaultCharacter(account.Name, server.Name);
                var selectedChar = available.Contains(savedChar ?? "") ? savedChar! : "any";

                var cmds = _loginService.GetCharacterCommands(account.Name, server.Name, selectedChar);

                var lines = string.IsNullOrWhiteSpace(cmds)
                    ? []
                    : cmds.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var preview = lines.Length == 0
                    ? string.Empty
                    : string.Join("  |  ", lines.Take(3)) + (lines.Length > 3 ? $"  (+{lines.Length - 3} more)" : string.Empty);

                _entries.Add(new CharacterCommandEntry
                {
                    AccountName = account.Name,
                    ServerName = server.Name,
                    CharacterName = selectedChar,
                    AvailableCharacters = available,
                    CommandCount = lines.Length,
                    CommandsPreview = preview
                });
            }
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshEntriesAsync();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var selections = _entries.Select(entry =>
            (entry.AccountName, entry.ServerName, entry.CharacterName ?? "any"));
        _loginService.SaveAllDefaultCharacters(selections);
        StatusText.Text = "Default characters saved.";
    }

    private void EditCommands_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CharacterCommandEntry entry })
        {
            var charName = entry.CharacterName ?? "any";
            var commands = _loginService.GetCharacterCommands(entry.AccountName, entry.ServerName, charName);
            var window = new CharacterLoginCommandsEditorWindow(entry.AccountName, entry.ServerName, charName, commands)
            {
                Owner = this
            };
            if (window.ShowDialog() == true)
            {
                _loginService.SetCharacterCommands(entry.AccountName, entry.ServerName, charName, window.Commands, window.WaitMs);
                var lines = string.IsNullOrWhiteSpace(window.Commands)
                    ? []
                    : window.Commands.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                entry.CommandCount = lines.Length;
                entry.CommandsPreview = lines.Length == 0
                    ? string.Empty
                    : string.Join("  |  ", lines.Take(3)) + (lines.Length > 3 ? $"  (+{lines.Length - 3} more)" : string.Empty);
                CharacterGrid.Items.Refresh();
            }
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view.View.Refresh();
        StatusText.Text = string.Empty;
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var first = _view.View.Cast<CharacterCommandEntry>().FirstOrDefault();
        if (first is null) return;
        CharacterGrid.SelectedItem = first;
        CharacterGrid.ScrollIntoView(first);
        CharacterGrid.Focus();
        e.Handled = true;
    }

    private void OffsetFromOwner()
    {
        if (Owner is null) return;
        WindowPositionHelper.RestoreOrOffset(this, Owner);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public class CharacterCommandEntry
{
    public string AccountName { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string CharacterName { get; set; } = "any";
    public List<string> AvailableCharacters { get; set; } = ["any"];
    public int CommandCount { get; set; }
    public string CommandsPreview { get; set; } = string.Empty;
}
