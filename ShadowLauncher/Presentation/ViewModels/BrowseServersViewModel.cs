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
    private string _statusText = "Loading server list...";
    private bool _isLoading;
    private Server? _selectedServer;
    private string _searchText = string.Empty;

    public BrowseServersViewModel(ServerListDownloader downloader)
    {
        _downloader = downloader;
        AddSelectedCommand = new RelayCommand(AddSelected, () => SelectedServer is not null);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);

        FilteredServers = CollectionViewSource.GetDefaultView(Servers);
        FilteredServers.Filter = FilterServer;
    }

    public event EventHandler<Server>? ServerAdded;

    public ObservableCollection<Server> Servers { get; } = [];

    /// <summary>The filtered view bound to the DataGrid.</summary>
    public ICollectionView FilteredServers { get; }

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

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "Downloading server list...";
        try
        {
            var servers = await _downloader.FetchServersAsync();
            Servers.Clear();
            foreach (var s in servers)
                Servers.Add(s);
            StatusText = $"Found {Servers.Count} servers";
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
