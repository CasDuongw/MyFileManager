using System.IO;

namespace FM.Core.FileSystem;

/// <summary>
/// Fast, deterministic in-memory filesystem for unit testing file operations with ZERO disk I/O.
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public InMemoryFileSystem()
    {
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        string normalized = path.Replace('/', '\\').TrimEnd('\\');
        return normalized;
    }

    public bool FileExists(string path)
    {
        lock (_lock)
        {
            return _files.Contains(Normalize(path));
        }
    }

    public bool DirectoryExists(string path)
    {
        lock (_lock)
        {
            return _directories.Contains(Normalize(path));
        }
    }

    public bool Exists(string path)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            return _files.Contains(n) || _directories.Contains(n);
        }
    }

    public void CreateDirectory(string path)
    {
        lock (_lock)
        {
            string current = Normalize(path);
            while (!string.IsNullOrEmpty(current))
            {
                _directories.Add(current);
                int lastSlash = current.LastIndexOf('\\');
                if (lastSlash <= 0) break;
                current = current.Substring(0, lastSlash);
            }
        }
    }

    public void CreateFile(string path)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            string? dir = Path.GetDirectoryName(n);
            if (!string.IsNullOrEmpty(dir))
            {
                CreateDirectory(dir);
            }
            _files.Add(n);
        }
    }

    public void DeleteDirectory(string path, bool recursive = false)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            if (!_directories.Contains(n))
            {
                throw new DirectoryNotFoundException($"Directory not found: '{path}'");
            }

            bool hasChildren = _files.Any(f => IsChild(n, f)) || _directories.Any(d => IsChild(n, d));
            if (hasChildren && !recursive)
            {
                throw new IOException($"Directory is not empty: '{path}'");
            }

            _directories.Remove(n);
            if (recursive)
            {
                _files.RemoveWhere(f => IsChild(n, f));
                _directories.RemoveWhere(d => IsChild(n, d));
            }
        }
    }

    public void DeleteFile(string path)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            if (!_files.Remove(n))
            {
                throw new FileNotFoundException($"File not found: '{path}'");
            }
        }
    }

    public void MoveFile(string sourcePath, string destinationPath)
    {
        lock (_lock)
        {
            string src = Normalize(sourcePath);
            string dst = Normalize(destinationPath);

            if (!_files.Contains(src))
            {
                throw new FileNotFoundException($"Source file not found: '{sourcePath}'");
            }
            if (_files.Contains(dst) || _directories.Contains(dst))
            {
                throw new IOException($"Destination already exists: '{destinationPath}'");
            }

            string? dstDir = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dstDir) && !_directories.Contains(dstDir))
            {
                throw new DirectoryNotFoundException($"Destination directory not found: '{dstDir}'");
            }

            _files.Remove(src);
            _files.Add(dst);
        }
    }

    public void MoveDirectory(string sourcePath, string destinationPath)
    {
        lock (_lock)
        {
            string src = Normalize(sourcePath);
            string dst = Normalize(destinationPath);

            if (!_directories.Contains(src))
            {
                throw new DirectoryNotFoundException($"Source directory not found: '{sourcePath}'");
            }
            if (_files.Contains(dst) || _directories.Contains(dst))
            {
                throw new IOException($"Destination already exists: '{destinationPath}'");
            }

            string? dstParent = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dstParent) && !_directories.Contains(dstParent))
            {
                throw new DirectoryNotFoundException($"Destination parent directory not found: '{dstParent}'");
            }

            // Move the directory itself
            _directories.Remove(src);
            _directories.Add(dst);

            // Move all nested subdirectories
            string srcPrefix = src + "\\";
            string dstPrefix = dst + "\\";

            var subDirs = _directories.Where(d => d.StartsWith(srcPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var d in subDirs)
            {
                _directories.Remove(d);
                _directories.Add(dstPrefix + d.Substring(srcPrefix.Length));
            }

            // Move all nested files
            var files = _files.Where(f => f.StartsWith(srcPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var f in files)
            {
                _files.Remove(f);
                _files.Add(dstPrefix + f.Substring(srcPrefix.Length));
            }
        }
    }

    public bool IsDirectoryEmpty(string path)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            if (!_directories.Contains(n)) return true;
            return !_files.Any(f => IsChild(n, f)) && !_directories.Any(d => IsChild(n, d));
        }
    }

    public IEnumerable<string> EnumerateEntries(string path)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            var directDirs = _directories.Where(d => IsDirectChild(n, d));
            var directFiles = _files.Where(f => IsDirectChild(n, f));
            return directDirs.Concat(directFiles).ToList();
        }
    }

    private static bool IsChild(string parent, string candidate)
    {
        if (candidate.Length <= parent.Length) return false;
        return candidate.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectChild(string parent, string candidate)
    {
        if (!IsChild(parent, candidate)) return false;
        string relative = candidate.Substring(parent.Length + 1);
        return !relative.Contains('\\');
    }
}

