using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace FM.App.Helpers;

/// <summary>
/// Helper extensions for traversing WPF visual and logical trees.
/// </summary>
public static class VisualTreeHelperExtensions
{
    public static T? FindVisualAncestor<T>(this DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            if (current is Visual or Visual3D)
            {
                current = VisualTreeHelper.GetParent(current);
            }
            else
            {
                current = LogicalTreeHelper.GetParent(current);
            }
        }

        return null;
    }
}
