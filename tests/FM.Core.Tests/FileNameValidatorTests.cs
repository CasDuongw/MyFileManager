using FM.Core.Operations;

namespace FM.Core.Tests;

public class FileNameValidatorTests
{
    [Theory]
    [InlineData("valid_file.txt")]
    [InlineData("Folder Name")]
    [InlineData("file-123_abc.tar.gz")]
    [InlineData("a")]
    [InlineData("My Project (2)")]
    public void ValidNames_PassValidation(string name)
    {
        bool isValid = FileNameValidator.TryValidate(name, out string? error);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void EmptyOrWhitespace_FailsValidation(string? name)
    {
        bool isValid = FileNameValidator.TryValidate(name, out string? error);
        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("file/name.txt")]
    [InlineData("file\\name.txt")]
    [InlineData("file:name.txt")]
    [InlineData("file*name.txt")]
    [InlineData("file?name.txt")]
    [InlineData("file\"name.txt")]
    [InlineData("file<name.txt")]
    [InlineData("file>name.txt")]
    [InlineData("file|name.txt")]
    public void InvalidCharacters_FailValidation(string name)
    {
        bool isValid = FileNameValidator.TryValidate(name, out string? error);
        Assert.False(isValid);
        Assert.Contains("cannot contain", error);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("aux.txt")]
    [InlineData("NUL")]
    [InlineData("nul.tar.gz")]
    [InlineData("COM1")]
    [InlineData("com9.dat")]
    [InlineData("LPT1")]
    [InlineData("lpt8.log")]
    public void ReservedDeviceNames_FailValidation(string name)
    {
        bool isValid = FileNameValidator.TryValidate(name, out string? error);
        Assert.False(isValid);
        Assert.Contains("reserved Windows device name", error);
    }

    [Theory]
    [InlineData("trailing_dot.")]
    [InlineData("ends_with_space ")]
    [InlineData("both. ")]
    public void TrailingDotOrSpace_FailsValidation(string name)
    {
        bool isValid = FileNameValidator.TryValidate(name, out string? error);
        Assert.False(isValid);
        Assert.Contains("cannot end with a period or space", error);
    }

    [Fact]
    public void NameExceeding255Chars_FailsValidation()
    {
        string longName = new string('a', 256);
        bool isValid = FileNameValidator.TryValidate(longName, out string? error);
        Assert.False(isValid);
        Assert.Contains("cannot exceed 255 characters", error);
    }
}
