using System.IO;
using FM.Core.FileSystem;
using FM.Core.ShellIntegration;

namespace FM.App.Tests.Fakes;

public class FakeShellNewService : IShellNewService
{
    private readonly List<ShellNewTemplate> _templates = new();
    public List<ShellNewTemplate> Templates => _templates;

    public FakeShellNewService(IEnumerable<ShellNewTemplate>? templates = null)
    {
        if (templates != null)
        {
            _templates.AddRange(templates);
        }
        else
        {
            _templates.Add(new ShellNewTemplate(".txt", "Text Document", ShellNewKind.NullFile));
            _templates.Add(new ShellNewTemplate(".dat", "Binary Data File", ShellNewKind.Data, TemplateData: new byte[] { 1, 2, 3, 4 }));
        }
    }

    public IReadOnlyList<ShellNewTemplate> GetTemplates() => _templates.ToList();

    public Task<string> CreateFileFromTemplateAsync(
        ShellNewTemplate template,
        string destinationDirectory,
        IFileSystem fileSystem,
        CancellationToken ct = default)
    {
        string ext = template.Extension.StartsWith('.') ? template.Extension : "." + template.Extension;
        string baseName = $"New {template.DisplayName}{ext}";
        string targetPath = Path.Combine(destinationDirectory, baseName);

        int counter = 2;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(baseName);
        while (fileSystem.FileExists(targetPath) || fileSystem.DirectoryExists(targetPath))
        {
            targetPath = Path.Combine(destinationDirectory, $"{nameWithoutExt} ({counter++}){ext}");
        }

        if (template.Kind == ShellNewKind.Data && template.TemplateData != null)
        {
            using var stream = fileSystem.CreateFileForWrite(targetPath);
            stream.Write(template.TemplateData, 0, template.TemplateData.Length);
        }
        else
        {
            using (fileSystem.CreateFileForWrite(targetPath)) { }
        }

        return Task.FromResult(targetPath);
    }
}
