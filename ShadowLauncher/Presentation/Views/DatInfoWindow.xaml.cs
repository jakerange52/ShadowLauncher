using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace ShadowLauncher.Presentation.Views;

public partial class DatInfoWindow : Window
{
    private string _fullText = string.Empty;

    public DatInfoWindow(string serverName, string cacheDir)
    {
        InitializeComponent();
        TitleText.Text = $"DAT Info — {serverName}";
        Loaded += async (_, _) => await PopulateAsync(serverName, cacheDir);
    }

    private async Task PopulateAsync(string serverName, string cacheDir)
    {
        SubtitleText.Text = cacheDir;

        if (!Directory.Exists(cacheDir))
        {
            InfoBox.Text = "DAT cache directory not found. Launch the game once to download DATs.";
            StatusText.Text = "No cache";
            return;
        }

        var files = Directory.GetFiles(cacheDir, "*", SearchOption.AllDirectories)
                             .OrderBy(f => f)
                             .ToList();

        // Show metadata immediately — no waiting on hashes
        var sb = new StringBuilder();
        sb.AppendLine($"Server:    {serverName}");
        sb.AppendLine($"Directory: {cacheDir}");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine(new string('─', 72));

        long totalBytes = 0;
        var fileInfos = new List<(string relativePath, FileInfo info, string placeholder)>();
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            var relativePath = Path.GetRelativePath(cacheDir, file);
            totalBytes += info.Length;
            fileInfos.Add((relativePath, info, file));

            sb.AppendLine();
            sb.AppendLine($"File:     {relativePath}");
            sb.AppendLine($"Size:     {FormatSize(info.Length)} ({info.Length:N0} bytes)");
            sb.AppendLine($"Modified: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"SHA-256:  computing...");
        }

        sb.AppendLine();
        sb.AppendLine(new string('─', 72));
        sb.AppendLine($"Total: {files.Count} file(s), {FormatSize(totalBytes)}");

        _fullText = sb.ToString();
        InfoBox.Text = _fullText;
        StatusText.Text = $"{files.Count} file(s) · {FormatSize(totalBytes)} · computing hashes…";

        // Hash each file on a background thread, updating in-place as each completes
        foreach (var (relativePath, info, filePath) in fileInfos)
        {
            var hash = await Task.Run(() => ComputeSha256(filePath));
            _fullText = _fullText.Replace(
                $"File:     {relativePath}\r\nSize:     {FormatSize(info.Length)} ({info.Length:N0} bytes)\r\nModified: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}\r\nSHA-256:  computing...",
                $"File:     {relativePath}\r\nSize:     {FormatSize(info.Length)} ({info.Length:N0} bytes)\r\nModified: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}\r\nSHA-256:  {hash}");
            InfoBox.Text = _fullText;
        }

        StatusText.Text = $"{files.Count} file(s) · {FormatSize(totalBytes)}";
    }

    private static string ComputeSha256(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexStringLower(hash);
        }
        catch (Exception ex)
        {
            return $"(error: {ex.Message})";
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F2} MB",
        >= 1_024         => $"{bytes / 1_024.0:F1} KB",
        _                => $"{bytes} B",
    };

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(InfoBox.Text);
        StatusText.Text = "Copied to clipboard ✓";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
