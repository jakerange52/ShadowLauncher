using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ShadowLauncher.Presentation.ViewModels;

namespace ShadowLauncher.Presentation.Views;

/// <summary>Converts bool to Visibility, inverted (true → Collapsed, false → Visible).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Modal progress window shown while DAT files are being downloaded or extracted.
/// Cannot be closed by the user — it closes itself once the operation completes.
/// </summary>
public partial class DatFetchWindow : Window
{
    public DatFetchViewModel ViewModel { get; }

    public DatFetchWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
        ViewModel = new DatFetchViewModel();
        DataContext = ViewModel;
    }
}
