using System;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FM.Core.Search;

namespace FM.App.Services.QuickOpen;

public partial class QuickOpenItemViewModel : ObservableObject
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Badge { get; set; } = string.Empty;
    public ImageSource? Icon { get; set; }
    public string? Glyph { get; set; }
    public QuickOpenItemKind Kind { get; set; } = QuickOpenItemKind.Place;
    public string? TargetPath { get; set; }
    public Action? ExecuteAction { get; set; }
    public int Priority { get; set; }
    public double Score { get; set; }

    private string? _normTitle;
    public string NormTitle
    {
        get => _normTitle ??= NameMatcher.Normalize(Title);
        set => _normTitle = value;
    }

    private ulong? _bitmask;
    public ulong Bitmask
    {
        get => _bitmask ??= CharacterBitmask.Compute(NormTitle);
        set => _bitmask = value;
    }

    private string? _rawParent;
    public string? RawParent
    {
        get => _rawParent ??= !string.IsNullOrEmpty(TargetPath) ? Path.GetDirectoryName(TargetPath) : null;
        set => _rawParent = value;
    }

    private string? _normParent;
    public string? NormParent
    {
        get => _normParent ??= !string.IsNullOrEmpty(RawParent) ? NameMatcher.Normalize(RawParent) : null;
        set => _normParent = value;
    }

    [ObservableProperty]
    private bool _isAvailable = true;

    [ObservableProperty]
    private bool _isSelected;
}

