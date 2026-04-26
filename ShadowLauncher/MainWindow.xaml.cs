using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Presentation.ViewModels;
using ShadowLauncher.Presentation.Views;
using ShadowLauncher.Services.Dats;

namespace ShadowLauncher;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IConfigurationProvider _config;
    private readonly IDatSetService _datSetService;

    private bool _suppressServerSelection;
    private bool _suppressAccountSelection;

    public MainWindow(MainWindowViewModel viewModel, IConfigurationProvider config, IDatSetService datSetService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _config = config;
        _datSetService = datSetService;
        DataContext = _viewModel;
        _viewModel.BrowseGameClientRequested += OnBrowseGameClient;
        _viewModel.ServerSelectionRestoreRequested += RestoreServerSelection;
        _viewModel.ProfileSelectionRestoreRequested += RestoreProfileSelection;
        Loaded += async (_, _) => await _viewModel.LoadAsync();
    }

    private void RestoreProfileSelection(IReadOnlyList<string> accountIds, IReadOnlyList<string> serverIds)
    {
        // Restore accounts
        _suppressAccountSelection = true;
        AccountsListBox.SelectedItems.Clear();
        foreach (Account account in AccountsListBox.Items)
            if (accountIds.Contains(account.Id))
                AccountsListBox.SelectedItems.Add(account);
        _suppressAccountSelection = false;
        _viewModel.SelectedAccounts.Clear();
        foreach (Account item in AccountsListBox.SelectedItems)
            _viewModel.SelectedAccounts.Add(item);

        // Restore servers
        _suppressServerSelection = true;
        ServersListBox.SelectedItems.Clear();
        foreach (Server server in ServersListBox.Items)
            if (serverIds.Contains(server.Id))
                ServersListBox.SelectedItems.Add(server);
        _suppressServerSelection = false;
        _viewModel.SelectedServers.Clear();
        foreach (Server item in ServersListBox.SelectedItems)
            _viewModel.SelectedServers.Add(item);
    }

    private void RestoreServerSelection(IReadOnlyList<string> ids)
    {
        _suppressServerSelection = true;
        ServersListBox.SelectedItems.Clear();
        foreach (Server server in ServersListBox.Items)
            if (ids.Contains(server.Id))
                ServersListBox.SelectedItems.Add(server);
        _suppressServerSelection = false;
        // Re-sync ViewModel
        _viewModel.SelectedServers.Clear();
        foreach (Server item in ServersListBox.SelectedItems)
            _viewModel.SelectedServers.Add(item);
    }

    private void TimeoutTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            var binding = System.Windows.Data.BindingOperations.GetBindingExpression(tb, TextBox.TextProperty);
            binding?.UpdateSource();
            Keyboard.ClearFocus();
        }
    }

    private void AccountsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAccountSelection) return;
        _viewModel.SelectedAccounts.Clear();
        foreach (Account item in ((ListBox)sender).SelectedItems)
            _viewModel.SelectedAccounts.Add(item);
    }

    private void ServersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressServerSelection) return;
        _viewModel.SelectedServers.Clear();
        foreach (Server item in ((ListBox)sender).SelectedItems)
            _viewModel.SelectedServers.Add(item);
    }

    /// <summary>
    /// Toggle-select: left click selects, next left click deselects — no Ctrl needed.
    /// </summary>
    private void ToggleSelect_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var listBox = (ListBox)sender;
        // Find the ListBoxItem under the mouse
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element is not ListBoxItem)
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);

        if (element is not ListBoxItem item) return;

        // Don't intercept clicks on buttons inside the item template
        var source = e.OriginalSource as DependencyObject;
        while (source is not null && source != item)
        {
            if (source is Button) return;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        // Toggle selection
        if (item.IsSelected)
            listBox.SelectedItems.Remove(item.DataContext);
        else
            listBox.SelectedItems.Add(item.DataContext);

        e.Handled = true;
    }

    private void OnBrowseGameClient(object? sender, EventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Game Client|acclient.exe|All Files|*.*",
            Title = "Select Game Client (acclient.exe)"
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.GameClientPath = dialog.FileName;
        }
    }

    private async void RemoveAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Account account })
            await _viewModel.RemoveAccountAsync(account);
    }

    private async void EditNote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Account account })
        {
            var dialog = new EditNoteWindow(account.Name, account.Notes)
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true && dialog.NoteText != account.Notes)
            {
                account.Notes = dialog.NoteText;
                await _viewModel.UpdateAccountNoteAsync(account);
            }
        }
    }

    private async void RemoveServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Server server })
            await _viewModel.RemoveServerAsync(server);
    }

    private void ServerDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Server server })
        {
            var detailsWindow = new Presentation.Views.ServerDetailsWindow(server, this, _config, _datSetService);
            detailsWindow.ServerEdited += async (_, updated) =>
                await _viewModel.UpdateServerAsync(updated);
            detailsWindow.ShowDialog();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}