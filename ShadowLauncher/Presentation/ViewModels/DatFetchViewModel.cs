using ShadowLauncher.Services.Dats;

namespace ShadowLauncher.Presentation.ViewModels;

public class DatFetchViewModel : ViewModelBase
{
    private string _statusText = "Preparing…";
    private string _fileName = string.Empty;
    private int _overallProgress;
    private int _fileProgress;
    private bool _isExtracting;

    /// <summary>Top-level status line, e.g. "Downloading DAT files (1 of 1)".</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>Name of the file currently being downloaded or extracted.</summary>
    public string FileName
    {
        get => _fileName;
        set => SetProperty(ref _fileName, value);
    }

    /// <summary>Overall batch progress 0–100.</summary>
    public int OverallProgress
    {
        get => _overallProgress;
        set => SetProperty(ref _overallProgress, value);
    }

    /// <summary>Per-file byte progress 0–100.</summary>
    public int FileProgress
    {
        get => _fileProgress;
        set => SetProperty(ref _fileProgress, value);
    }

    /// <summary>True while extracting a zip (hides per-file bar, shows indeterminate).</summary>
    public bool IsExtracting
    {
        get => _isExtracting;
        set => SetProperty(ref _isExtracting, value);
    }

    /// <summary>
    /// Updates all bound properties from a <see cref="DatDownloadProgress"/> report.
    /// Safe to call from any thread (dispatched by <see cref="System.Windows.Application.Current"/>).
    /// </summary>
    public void Apply(DatDownloadProgress p)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            FileName = p.FileName;
            IsExtracting = false;

            if (p.TotalBytes > 0)
            {
                var pct = (int)Math.Clamp((p.BytesReceived * 100.0) / p.TotalBytes, 0, 100);
                OverallProgress = pct;
                FileProgress = pct;

                var receivedMb = p.BytesReceived / 1_048_576.0;
                var totalMb = p.TotalBytes / 1_048_576.0;
                StatusText = totalMb > 0
                    ? $"Downloading… {receivedMb:F0} MB / {totalMb:F0} MB"
                    : $"Downloading… {receivedMb:F0} MB";

                // Once bytes are fully received, switch to extracting state
                // (extraction happens synchronously after the download).
                if (p.BytesReceived >= p.TotalBytes)
                    SetExtracting(p.FileName);
            }
            else
            {
                StatusText = "Downloading DAT files…";
                FileProgress = 0;
            }
        });
    }

    public void SetExtracting(string zipName)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            FileName = zipName;
            StatusText = "Extracting DAT archive…";
            IsExtracting = true;
            FileProgress = 0;
        });
    }

    public void SetComplete()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            StatusText = "Done.";
            OverallProgress = 100;
            FileProgress = 100;
            IsExtracting = false;
        });
    }
}
