using FM.App.Services;

namespace FM.App.Tests.Fakes;

public sealed class FakeInputDialogService : IInputDialogService
{
    public string? NextResult { get; set; }
    public string? LastTitle { get; private set; }
    public string? LastMessage { get; private set; }
    public string? LastInitialValue { get; private set; }
    public int? LastSelectionLength { get; private set; }

    public string? Prompt(string title, string message, string initialValue = "", int? selectionLength = null)
    {
        LastTitle = title;
        LastMessage = message;
        LastInitialValue = initialValue;
        LastSelectionLength = selectionLength;
        return NextResult ?? initialValue;
    }
}

