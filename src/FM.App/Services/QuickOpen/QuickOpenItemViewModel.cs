using System;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

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

    [ObservableProperty]
    private bool _isAvailable = true;

    [ObservableProperty]
    private bool _isSelected;
}

