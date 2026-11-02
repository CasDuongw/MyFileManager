using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FM.Core.FileSystem;
using FM.Core.ShellIntegration;
using Microsoft.Win32;

namespace FM.App.Services;

/// <summary>
/// Discovers and materializes Windows ShellNew templates (supporting NullFile, FileName, and Data only; never Command).
/// </summary>
public class WindowsShellNewService : IShellNewService
{
    private List<ShellNewTemplate>? _cachedTemplates;
    private readonly object _lock = new();

    public IReadOnlyList<ShellNewTemplate> GetTemplates()
    {
        lock (_lock)
        {
            if (_cachedTemplates != null) return _cachedTemplates;

            var list = new List<ShellNewTemplate>();
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    list = LoadRegistryTemplates();
                }
                catch
                {
                    // Fall back to built-in templates if registry fails
                }
            }

            // Always ensure standard basic templates are present
            EnsureDefaultTemplates(list);
            _cachedTemplates = list;
            return _cachedTemplates;
        }
    }

    [SupportedOSPlatform("windows")]
    private static List<ShellNewTemplate> LoadRegistryTemplates()
    {
        var result = new List<ShellNewTemplate>();
        var seenExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Standard common extensions to check first for high-priority templates
        string[] priorityExts = { ".txt", ".docx", ".xlsx", ".pptx", ".rtf", ".bmp", ".zip" };

        foreach (var ext in priorityExts)
        {
            var tmpl = TryReadShellNew(ext);
            if (tmpl != null && seenExtensions.Add(tmpl.Extension))
            {
                result.Add(tmpl);
            }
        }

        try
        {
            using var cr = Registry.ClassesRoot;
            foreach (var subKeyName in cr.GetSubKeyNames())
            {
                if (!subKeyName.StartsWith('.') || subKeyName.Length <= 1) continue;
                if (seenExtensions.Contains(subKeyName)) continue;

                var tmpl = TryReadShellNew(subKeyName);
                if (tmpl != null && seenExtensions.Add(tmpl.Extension))
                {
                    result.Add(tmpl);
                    if (result.Count >= 20) break; // Bounded store
                }
            }
        }
        catch { }

        return result;
    }

    [SupportedOSPlatform("windows")]
    private static ShellNewTemplate? TryReadShellNew(string ext)
    {
        try
        {
            using var extKey = Registry.ClassesRoot.OpenSubKey(ext);
            if (extKey == null) return null;

            // 1. Direct HKCR\<.ext>\ShellNew
            using var directShellNew = extKey.OpenSubKey("ShellNew");
            if (directShellNew != null)
            {
                var t = ParseShellNewKey(ext, directShellNew, extKey);
                if (t != null) return t;
            }

            // 2. HKCR\<.ext>\<ProgID>\ShellNew
            string? progId = extKey.GetValue("") as string;
            if (!string.IsNullOrEmpty(progId))
            {
                using var progKey = Registry.ClassesRoot.OpenSubKey(progId);
                if (progKey != null)
                {
                    using var progShellNew = progKey.OpenSubKey("ShellNew");
                    if (progShellNew != null)
                    {
                        var t = ParseShellNewKey(ext, progShellNew, progKey);
                        if (t != null) return t;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static ShellNewTemplate? ParseShellNewKey(string ext, RegistryKey shellNewKey, RegistryKey parentKey)
    {
        // STRICT SAFETY: Do NOT support Command key type!
        if (shellNewKey.GetValue("Command") != null)
        {
            return null;
        }

        string displayName = parentKey.GetValue("") as string ?? ext.TrimStart('.').ToUpperInvariant() + " Document";

        // 1. NullFile
        if (shellNewKey.GetValue("NullFile") != null)
        {
            return new ShellNewTemplate(ext, displayName, ShellNewKind.NullFile);
        }

        // 2. Data
        if (shellNewKey.GetValue("Data") is byte[] dataBytes && dataBytes.Length > 0)
        {
            return new ShellNewTemplate(ext, displayName, ShellNewKind.Data, TemplateData: dataBytes);
        }

        // 3. FileName
        if (shellNewKey.GetValue("FileName") is string fileName && !string.IsNullOrWhiteSpace(fileName))
        {
            return new ShellNewTemplate(ext, displayName, ShellNewKind.FileName, TemplateFileName: fileName);
        }

        return null;
    }

    private static void EnsureDefaultTemplates(List<ShellNewTemplate> list)
    {
        if (!list.Any(t => string.Equals(t.Extension, ".txt", StringComparison.OrdinalIgnoreCase)))
        {
            list.Insert(0, new ShellNewTemplate(".txt", "Text Document", ShellNewKind.NullFile));
        }
    }

    public async Task<string> CreateFileFromTemplateAsync(
        ShellNewTemplate template,
        string destinationDirectory,
        IFileSystem fileSystem,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string ext = template.Extension.StartsWith('.') ? template.Extension : "." + template.Extension;
        string baseName = string.IsNullOrWhiteSpace(template.DisplayName)
            ? $"New Document{ext}"
            : $"New {template.DisplayName}{ext}";

        // Sanitize base name
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            baseName = baseName.Replace(invalid, '_');
        }

        string targetPath = Path.Combine(destinationDirectory, baseName);
        int counter = 2;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(baseName);

        while (fileSystem.FileExists(targetPath) || fileSystem.DirectoryExists(targetPath))
        {
            string candidate = $"{nameWithoutExt} ({counter++}){ext}";
            targetPath = Path.Combine(destinationDirectory, candidate);
        }

        if (template.Kind == ShellNewKind.NullFile || (template.Kind == ShellNewKind.Data && template.TemplateData == null))
        {
            using (fileSystem.CreateFileForWrite(targetPath)) { }
        }
        else if (template.Kind == ShellNewKind.Data && template.TemplateData != null)
        {
            using var stream = fileSystem.CreateFileForWrite(targetPath);
            await stream.WriteAsync(template.TemplateData, ct).ConfigureAwait(false);
        }
        else if (template.Kind == ShellNewKind.FileName && !string.IsNullOrEmpty(template.TemplateFileName))
        {
            byte[]? fileBytes = null;
            if (OperatingSystem.IsWindows())
            {
                string sysShellNew = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ShellNew", template.TemplateFileName);
                if (File.Exists(sysShellNew))
                {
                    fileBytes = await File.ReadAllBytesAsync(sysShellNew, ct).ConfigureAwait(false);
                }
            }

            if (fileBytes != null)
            {
                using var stream = fileSystem.CreateFileForWrite(targetPath);
                await stream.WriteAsync(fileBytes, ct).ConfigureAwait(false);
            }
            else
            {
                using (fileSystem.CreateFileForWrite(targetPath)) { }
            }
        }
        else
        {
            using (fileSystem.CreateFileForWrite(targetPath)) { }
        }

        return targetPath;
    }
}
