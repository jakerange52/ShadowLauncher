using System.Windows;
using ShadowLauncher.Presentation.ViewModels;

namespace ShadowLauncher.Presentation.Views;

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
