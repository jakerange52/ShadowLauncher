using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.WebServices;

namespace ShadowLauncher.Presentation.ViewModels;

/// <summary>Row wrapper combining a <see cref="Server"/> with its live TreeStats player count.</summary>
public sealed class ServerRow(Server server, PlayerCount? count)
{
    public Server Server { get; } = server;
    public string PlayerCountDisplay { get; } = count is null ? "—" : count.Count.ToString();
    public string? PlayerCountAge { get; } = count?.Age;
    public int PlayerCountValue { get; } = count?.Count ?? -1;
}

public class BrowseServersViewModel : ViewModelBase
{
    private readonly ServerListDownloader _downloader;
    private readonly BetaServerListDownloader _betaDownloader;
    private readonly TreeStatsService _treeStats;
    private string _statusText = "Loading server list...";
    private bool _isLoading;
    private bool _showingBeta;
    private ServerRow? _selectedRow;
    private string _searchText = string.Empty;

    public BrowseServersViewModel(ServerListDownloader downloader, BetaServerListDownloader betaDownloader, TreeStatsService treeStats)
    {
        _downloader = downloader;
        _betaDownloader = betaDownloader;
        _treeStats = treeStats;
        AddSelectedCommand = new RelayCommand(AddSelected, () => SelectedRow is not null);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        ToggleListCommand = new RelayCommand(ToggleList);

        FilteredServers = CollectionViewSource.GetDefaultView(Rows);
        FilteredServers.Filter = FilterServer;
    }

    public event EventHandler<Server>? ServerAdded;

    public ObservableCollection<ServerRow> Rows { get; } = [];
    public ICollectionView FilteredServers { get; }

    public bool ShowingBeta
    {
        get => _showingBeta;
        private set
        {
            if (SetProperty(ref _showingBeta, value))
                OnPropertyChanged(nameof(ToggleButtonContent));
        }
    }

    public string ToggleButtonContent => _showingBeta ? "◀ Regular Servers" : "β Beta Servers";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                FilteredServers.Refresh();
        }
    }

    public ServerRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
                OnPropertyChanged(nameof(SelectedServer));
        }
    }

    /// <summary>Convenience accessor for the selected underlying server.</summary>
    public Server? SelectedServer => _selectedRow?.Server;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public ICommand AddSelectedCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand ToggleListCommand { get; }

    public async Task LoadAsync()
    {
        await LoadCurrentListAsync();
    }

    private async Task LoadCurrentListAsync()
    {
        IsLoading = true;
        StatusText = _showingBeta ? "Downloading beta server list..." : "Downloading server list...";
        SelectedRow = null;
        try
        {
            var serversTask = _showingBeta
                ? _betaDownloader.FetchServersAsync()
                : _downloader.FetchServersAsync();
            var countsTask = _treeStats.GetPlayerCountsAsync();

            await Task.WhenAll(serversTask, countsTask);

            var counts = countsTask.Result;
            Rows.Clear();
            foreach (var s in serversTask.Result)
                Rows.Add(new ServerRow(s, counts.TryGetValue(s.Name, out var c) ? c : null));

            var label = _showingBeta ? "beta server" : "server";
            StatusText = $"Found {Rows.Count} {label}{(Rows.Count == 1 ? "" : "s")}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async void ToggleList()
    {
        ShowingBeta = !_showingBeta;
        SearchText = string.Empty;
        await LoadCurrentListAsync();
    }

    private void AddSelected()
    {
        if (SelectedServer is null) return;
        ServerAdded?.Invoke(this, SelectedServer);
    }

    private bool FilterServer(object obj)
    {
        if (string.IsNullOrWhiteSpace(_searchText)) return true;
        if (obj is not ServerRow row) return false;
        var s = row.Server;
        var q = _searchText.Trim();
        return Contains(s.Name, q)
            || Contains(s.Description, q)
            || Contains(s.Hostname, q)
            || Contains(s.Emulator.ToString(), q)
            || Contains(s.PublishedStatus, q)
            || Contains(s.Port.ToString(), q);
    }

    private static bool Contains(string? source, string query) =>
        source?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}
