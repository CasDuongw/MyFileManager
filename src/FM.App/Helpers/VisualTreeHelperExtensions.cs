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

    public static T? FindVisualChild<T>(this DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null) return descendant;
        }
        return null;
    }
}
