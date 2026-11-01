using System;

namespace FM.App.Services.QuickOpen;

public class AppCommand
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Category { get; init; }
    public string ShortcutText { get; init; } = string.Empty;
    public Func<bool> CanExecute { get; init; } = () => true;
    public Action Execute { get; init; } = () => { };
    public string? Glyph { get; init; }
}
