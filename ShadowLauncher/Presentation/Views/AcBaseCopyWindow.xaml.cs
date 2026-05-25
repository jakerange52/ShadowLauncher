using System.ComponentModel;
using System.Windows;
using ShadowLauncher.Application;

namespace ShadowLauncher.Presentation.Views;

/// <summary>
/// Modal progress window shown during the one-time ACBase directory copy.
/// Cannot be closed by the user — it closes itself when the copy completes.
/// </summary>
public partial class AcBaseCopyWindow : Window, INotifyPropertyChanged
{
    private string _currentFile = string.Empty;
    private double _progress;
    private string _countText = string.Empty;

    public string CurrentFile
    {
        get => _currentFile;
        private set { _currentFile = value; OnPropertyChanged(nameof(CurrentFile)); }
    }

    public double Progress
    {
        get => _progress;
        private set { _progress = value; OnPropertyChanged(nameof(Progress)); }
    }

    public string CountText
    {
        get => _countText;
        private set { _countText = value; OnPropertyChanged(nameof(CountText)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public AcBaseCopyWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>
    /// Runs the copy task, reporting progress into this window, then closes when done.
    /// </summary>
    public async Task RunAsync(Func<IProgress<AcBaseCopyProgress>, Task> copyTask)
    {
        var progress = new Progress<AcBaseCopyProgress>(p =>
        {
            CurrentFile = p.CurrentFile;
            CountText = p.FilesTotal > 0 ? $"{p.FilesCompleted} / {p.FilesTotal} files" : string.Empty;
            Progress = p.FilesTotal > 0 ? (double)p.FilesCompleted / p.FilesTotal * 100 : 0;
        });

        try
        {
            await copyTask(progress);
        }
        finally
        {
            Close();
        }
    }
}
