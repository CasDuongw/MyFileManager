using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FM.App.Helpers;
using FM.Core.Operations.Transfer;

namespace FM.App.Views;

/// <summary>
/// Interaction logic for ConflictDialog.xaml.
/// Provides side-by-side comparison of source and destination files with visual badges for newer/larger files,
/// warning notice for overwrite operations, and conflict resolution actions.
/// </summary>
public partial class ConflictDialog : Window
{
    public ConflictDecision Decision { get; private set; } = new(ConflictAction.Cancel);

    public ConflictDialog(ConflictInfo info)
    {
        InitializeComponent();

        FileNameText.Text = Path.GetFileName(info.SourcePath);

        SourceSizeText.Text = $"{FormatHelper.FormatFileSize(info.SourceSize, false)} ({info.SourceSize:N0} bytes)";
        DestSizeText.Text = $"{FormatHelper.FormatFileSize(info.ExistingSize, false)} ({info.ExistingSize:N0} bytes)";

        SourceModifiedText.Text = info.SourceModified == DateTime.MinValue
            ? "Unknown"
            : info.SourceModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        DestModifiedText.Text = info.ExistingModified == DateTime.MinValue
            ? "Unknown"
            : info.ExistingModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        // Compare modified dates (tolerance 1 second)
        var dateDiff = (info.SourceModified - info.ExistingModified).TotalSeconds;
        if (dateDiff > 1.0)
        {
            SetBadge(SourceDateBadge, SourceDateBadgeText, "Newer", "#DCFCE7", "#166534");
            SetBadge(DestDateBadge, DestDateBadgeText, "Older", "#F3F4F6", "#6B7280");
        }
        else if (dateDiff < -1.0)
        {
            SetBadge(DestDateBadge, DestDateBadgeText, "Newer", "#DCFCE7", "#166534");
            SetBadge(SourceDateBadge, SourceDateBadgeText, "Older", "#F3F4F6", "#6B7280");
        }

        // Compare sizes
        if (info.SourceSize > info.ExistingSize)
        {
            SetBadge(SourceSizeBadge, SourceSizeBadgeText, "Larger", "#DBEAFE", "#1E40AF");
            SetBadge(DestSizeBadge, DestSizeBadgeText, "Smaller", "#F3F4F6", "#6B7280");
        }
        else if (info.SourceSize < info.ExistingSize)
        {
            SetBadge(DestSizeBadge, DestSizeBadgeText, "Larger", "#DBEAFE", "#1E40AF");
            SetBadge(SourceSizeBadge, SourceSizeBadgeText, "Smaller", "#F3F4F6", "#6B7280");
        }

        Loaded += (s, e) => KeepBothButton.Focus();
    }

    private static void SetBadge(Border badge, TextBlock badgeText, string text, string bgHex, string fgHex)
    {
        badgeText.Text = text;
        badgeText.Foreground = (Brush)new BrushConverter().ConvertFromString(fgHex)!;
        badge.Background = (Brush)new BrushConverter().ConvertFromString(bgHex)!;
        badge.Visibility = Visibility.Visible;
    }

    private void OnKeepBothClicked(object sender, RoutedEventArgs e)
    {
        Decision = new ConflictDecision(ConflictAction.KeepBoth, ApplyToAllCheckBox.IsChecked == true);
        Close();
    }

    private void OnOverwriteClicked(object sender, RoutedEventArgs e)
    {
        Decision = new ConflictDecision(ConflictAction.Overwrite, ApplyToAllCheckBox.IsChecked == true);
        Close();
    }

    private void OnOverwriteIfNewerClicked(object sender, RoutedEventArgs e)
    {
        Decision = new ConflictDecision(ConflictAction.OverwriteIfNewer, ApplyToAllCheckBox.IsChecked == true);
        Close();
    }

    private void OnSkipClicked(object sender, RoutedEventArgs e)
    {
        Decision = new ConflictDecision(ConflictAction.Skip, ApplyToAllCheckBox.IsChecked == true);
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Decision = new ConflictDecision(ConflictAction.Cancel, ApplyToAllCheckBox.IsChecked == true);
        Close();
    }
}

