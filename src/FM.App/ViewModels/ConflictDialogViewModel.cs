using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FM.App.Helpers;
using FM.App.Services;
using FM.Core.Operations.Transfer;

namespace FM.App.ViewModels;

/// <summary>
/// Fully testable ViewModel for the interactive conflict resolution dialog.
/// Encapsulates side-by-side comparison, badges, dynamic backup warnings, and asynchronous thumbnails.
/// </summary>
public partial class ConflictDialogViewModel : ObservableObject
{
    private readonly IThumbnailService? _thumbnailService;
    private readonly CancellationTokenSource _cts = new();

    public ConflictInfo Info { get; }
    public string FileName { get; }
    public string SourceDirectory { get; }
    public string DestDirectory { get; }

    public string SourceSizeText { get; }
    public string DestSizeText { get; }
    public string SourceModifiedText { get; }
    public string DestModifiedText { get; }

    public bool SourceIsNewer { get; }
    public bool DestIsNewer { get; }
    public bool SourceIsLarger { get; }
    public bool DestIsLarger { get; }

    public string OverwriteIfNewerHint { get; }
    public bool OverwriteIsUndoable { get; }
    public string BannerText { get; }
    public bool BannerIsWarning { get; }

    [ObservableProperty]
    private bool _applyToAll;

    [ObservableProperty]
    private ImageSource? _sourceThumbnail;

    [ObservableProperty]
    private ImageSource? _destThumbnail;

    public ConflictDecision Decision { get; private set; }

    public event Action? RequestClose;

    public ConflictDialogViewModel(ConflictInfo info, IThumbnailService? thumbnailService = null)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
        _thumbnailService = thumbnailService;
        Decision = new ConflictDecision(ConflictAction.Cancel);

        FileName = Path.GetFileName(info.SourcePath);
        SourceDirectory = Path.GetDirectoryName(info.SourcePath) ?? string.Empty;
        DestDirectory = Path.GetDirectoryName(info.ExistingPath) ?? string.Empty;

        SourceSizeText = $"{FormatHelper.FormatFileSize(info.SourceSize, false)} ({info.SourceSize:N0} bytes)";
        DestSizeText = $"{FormatHelper.FormatFileSize(info.ExistingSize, false)} ({info.ExistingSize:N0} bytes)";

        bool sourceDateUnknown = (info.SourceModified == DateTime.MinValue);
        bool destDateUnknown = (info.ExistingModified == DateTime.MinValue);

        SourceModifiedText = sourceDateUnknown ? "Unknown" : info.SourceModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        DestModifiedText = destDateUnknown ? "Unknown" : info.ExistingModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        // Date comparison with 1-second tolerance
        if (!sourceDateUnknown && !destDateUnknown)
        {
            double diffSeconds = (info.SourceModified - info.ExistingModified).TotalSeconds;
            if (diffSeconds > 1.0)
            {
                SourceIsNewer = true;
            }
            else if (diffSeconds < -1.0)
            {
                DestIsNewer = true;
            }
        }

        // Size comparison
        if (info.SourceSize > info.ExistingSize)
        {
            SourceIsLarger = true;
        }
        else if (info.ExistingSize > info.SourceSize)
        {
            DestIsLarger = true;
        }

        // Overwrite if newer hint
        OverwriteIfNewerHint = SourceIsNewer
            ? "Incoming is newer → will overwrite"
            : (DestIsNewer ? "Existing is newer → will skip" : "Same age → will skip");

        // Dynamic Banner based on staging availability
        OverwriteIsUndoable = info.OverwriteIsUndoable;
        if (OverwriteIsUndoable)
        {
            BannerText = "The existing file will be saved to a safe backup. You can undo with Ctrl+Z (kept for 7 days).";
            BannerIsWarning = false;
        }
        else
        {
            BannerText = "Overwritten files cannot be restored with Undo (backup staging unavailable).";
            BannerIsWarning = true;
        }

        // Initiate asynchronous thumbnail fetch without blocking dialog display
        if (_thumbnailService != null)
        {
            _ = LoadThumbnailsAsync(_cts.Token);
        }
    }

    private async Task LoadThumbnailsAsync(CancellationToken ct)
    {
        try
        {
            var srcTask = _thumbnailService!.GetThumbnailAsync(Info.SourcePath, 96, ct);
            var dstTask = _thumbnailService!.GetThumbnailAsync(Info.ExistingPath, 96, ct);

            await Task.WhenAll(srcTask, dstTask).ConfigureAwait(true);

            SourceThumbnail = await srcTask;
            DestThumbnail = await dstTask;
        }
        catch
        {
            // Fall back to null placeholders
        }
    }

    public void CancelThumbnailLoading()
    {
        _cts.Cancel();
    }

    public void ChooseKeepBoth()
    {
        Decision = new ConflictDecision(ConflictAction.KeepBoth, ApplyToAll);
        RequestClose?.Invoke();
    }

    public void ChooseOverwriteIfNewer()
    {
        Decision = new ConflictDecision(ConflictAction.OverwriteIfNewer, ApplyToAll);
        RequestClose?.Invoke();
    }

    public void ChooseOverwrite()
    {
        Decision = new ConflictDecision(ConflictAction.Overwrite, ApplyToAll);
        RequestClose?.Invoke();
    }

    public void ChooseSkip()
    {
        Decision = new ConflictDecision(ConflictAction.Skip, ApplyToAll);
        RequestClose?.Invoke();
    }

    public void ChooseCancel()
    {
        Decision = new ConflictDecision(ConflictAction.Cancel, ApplyToAll);
        RequestClose?.Invoke();
    }
}

