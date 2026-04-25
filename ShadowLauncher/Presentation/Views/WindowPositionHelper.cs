using System.Windows;

namespace ShadowLauncher.Presentation.Views;

/// <summary>
/// Remembers the last screen position for each child window type and restores it
/// on next open, provided the saved position is still on-screen and within a
/// reasonable distance of the owner window. Falls back to ClampedOffset otherwise.
/// </summary>
internal static class WindowPositionHelper
{
    private static readonly Dictionary<string, (double Left, double Top)> _saved = new();

    /// <summary>Maximum distance (px) between saved window center and owner center.</summary>
    private const double MaxDistanceFromOwner = 800;

    /// <summary>Call from the window's Loaded handler.</summary>
    internal static void RestoreOrOffset(Window child, Window owner)
    {
        var key = child.GetType().Name;
        if (_saved.TryGetValue(key, out var pos) && IsReasonable(pos.Left, pos.Top, child, owner))
        {
            child.Left = pos.Left;
            child.Top  = pos.Top;
        }
        else
        {
            AddAccountWindow.ClampedOffset(child, owner);
        }
    }

    /// <summary>Call from the window's Closed handler.</summary>
    internal static void Save(Window child)
    {
        _saved[child.GetType().Name] = (child.Left, child.Top);
    }

    private static bool IsReasonable(double left, double top, Window child, Window owner)
    {
        var vLeft   = SystemParameters.VirtualScreenLeft;
        var vTop    = SystemParameters.VirtualScreenTop;
        var vRight  = vLeft + SystemParameters.VirtualScreenWidth;
        var vBottom = vTop  + SystemParameters.VirtualScreenHeight;

        // Entire window must fit on a screen
        if (left < vLeft || top < vTop ||
            left + child.Width  > vRight ||
            top  + child.Height > vBottom)
            return false;

        // Center of saved position must be within MaxDistanceFromOwner of owner center
        var ownerCX = owner.Left + owner.Width  / 2;
        var ownerCY = owner.Top  + owner.Height / 2;
        var childCX = left + child.Width  / 2;
        var childCY = top  + child.Height / 2;

        var dist = Math.Sqrt(Math.Pow(childCX - ownerCX, 2) + Math.Pow(childCY - ownerCY, 2));
        return dist <= MaxDistanceFromOwner;
    }
}
