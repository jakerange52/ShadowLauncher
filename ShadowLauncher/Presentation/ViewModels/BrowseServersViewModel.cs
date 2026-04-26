using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.WebServices;

namespace ShadowLauncher.Presentation.ViewModels;

public class BrowseServersViewModel : ViewModelBase
{
    private readonly ServerListDownloader _downloader;
    private readonly BetaServerListDownloader _betaDownloader;
    private string _statusText = "Loading server list...";
    private bool _isLoading;
    private bool _showingBeta;
    private Server? _selectedServer;
    private string _searchText = string.Empty;

    public BrowseServersViewModel(ServerListDownloader downloader, BetaServerListDownloader betaDownloader)
    {
        _downloader = downloader;
        _betaDownloader = betaDownloader;
        AddSelectedCommand = new RelayCommand(AddSelected, () => SelectedServer is not null);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        ToggleListCommand = new RelayCommand(ToggleList);

        FilteredServers = CollectionViewSource.GetDefaultView(Servers);
        FilteredServers.Filter = FilterServer;
    }

    public event EventHandler<Server>? ServerAdded;

    public ObservableCollection<Server> Servers { get; } = [];
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

    public Server? SelectedServer
    {
        get => _selectedServer;
        set => SetProperty(ref _selectedServer, value);
    }

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
        SelectedServer = null;
        try
        {
            var servers = _showingBeta
                ? await _betaDownloader.FetchServersAsync()
                : await _downloader.FetchServersAsync();
            Servers.Clear();
            foreach (var s in servers)
                Servers.Add(s);
            var label = _showingBeta ? "beta server" : "server";
            StatusText = $"Found {Servers.Count} {label}{(Servers.Count == 1 ? "" : "s")}";
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
        if (obj is not Server s) return false;
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
