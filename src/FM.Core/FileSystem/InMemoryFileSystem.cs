using System.IO;

namespace FM.Core.FileSystem;

/// <summary>
/// In-memory filesystem supporting virtual files (deterministic content with zero disk & zero RAM),
/// simulated volume boundaries, failure injection, and reparse points for rock-solid unit testing.
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    public class DirectoryData
    {
        public DateTime Modified { get; set; } = DateTime.UtcNow;
        public FileAttributes Attributes { get; set; } = FileAttributes.Directory;
        public bool IsReparsePoint { get; set; }
    }

    public class FileData
    {
        public long Length { get; set; }
        public byte[]? SmallContent { get; set; }
        public DateTime Modified { get; set; } = DateTime.UtcNow;
        public FileAttributes Attributes { get; set; } = FileAttributes.Normal;
        public bool IsReparsePoint { get; set; }
    }

    public class FailureRule
    {
        public string NormalizedPath { get; }
        public long ByteOffset { get; }
        public Exception Exception { get; }
        public int RemainingAttempts { get; set; }

        public FailureRule(string normalizedPath, long byteOffset, Exception exception, int attempts)
        {
            NormalizedPath = normalizedPath;
            ByteOffset = byteOffset;
            Exception = exception;
            RemainingAttempts = attempts;
        }
    }

    private readonly Dictionary<string, DirectoryData> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileData> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _volumeFreeSpace = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FailureRule> _failureRules = new();
    private readonly Dictionary<string, Exception> _enumerationFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public InMemoryFileSystem()
    {
        // Default root directories
        CreateDirectory(@"C:\");
        CreateDirectory(@"D:\");
    }

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        string normalized = path.Replace('/', '\\').TrimEnd('\\');
        if (normalized.Length == 2 && normalized[1] == ':')
        {
            normalized += "\\";
        }
        return normalized;
    }

    public static string GetVolumeRoot(string path)
    {
        string norm = Normalize(path);
        int colon = norm.IndexOf(':');
        if (colon > 0)
        {
            return norm.Substring(0, colon + 1).ToUpperInvariant() + "\\";
        }
        return @"C:\";
    }

    private static readonly byte[] DeterministicPattern = Enumerable.Range(0, 256).Select(i => (byte)((i * 31 + 7) & 0xFF)).ToArray();

    string IFileSystem.GetVolumeRoot(string path) => GetVolumeRoot(path);

    public void SetHidden(string path)
    {
        lock (_lock)
        {
            string norm = Normalize(path);
            if (_files.TryGetValue(norm, out var file))
            {
                file.Attributes |= FileAttributes.Hidden;
            }
            else if (_directories.TryGetValue(norm, out var dir))
            {
                dir.Attributes |= FileAttributes.Hidden;
            }
        }
    }

    public static byte GetDeterministicByte(long offset)
    {
        return DeterministicPattern[(int)(offset & 0xFF)];
    }

    // -------------------------------------------------------------------------
    // Volume & Free Space
    // -------------------------------------------------------------------------

    public void SetVolumeFreeSpace(string volumeRoot, long freeBytes)
    {
        lock (_lock)
        {
            string root = GetVolumeRoot(volumeRoot);
            _volumeFreeSpace[root] = freeBytes;
        }
    }

    public string GetVolumeId(string path)
    {
        string root = GetVolumeRoot(path);
        return $"MEM_VOL_{root.TrimEnd('\\', ':')}";
    }

    public long GetFreeSpace(string path)
    {
        lock (_lock)
        {
            string root = GetVolumeRoot(path);
            if (_volumeFreeSpace.TryGetValue(root, out long free))
            {
                return free;
            }
            return 100L * 1024 * 1024 * 1024; // 100 GB default
        }
    }

    // -------------------------------------------------------------------------
    // Failure Injection
    // -------------------------------------------------------------------------

    public void InjectFailure(string path, long byteOffset, Exception exception, int count = 1)
    {
        lock (_lock)
        {
            _failureRules.Add(new FailureRule(Normalize(path), byteOffset, exception, count));
        }
    }

    public void ClearFailures()
    {
        lock (_lock)
        {
            _failureRules.Clear();
        }
    }

    internal void CheckFailure(string normalizedPath, long currentOffset, int bytesRequested)
    {
        lock (_lock)
        {
            foreach (var rule in _failureRules)
            {
                if (rule.RemainingAttempts > 0 &&
                    string.Equals(rule.NormalizedPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (rule.ByteOffset >= currentOffset && rule.ByteOffset < currentOffset + bytesRequested)
                    {
                        rule.RemainingAttempts--;
                        throw rule.Exception;
                    }
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Virtual Files Creation
    // -------------------------------------------------------------------------

    public void CreateVirtualFile(string path, long length, DateTime? modified = null, FileAttributes attributes = FileAttributes.Normal)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            string? dir = Path.GetDirectoryName(n);
            if (!string.IsNullOrEmpty(dir))
            {
                CreateDirectory(dir);
            }

            _files[n] = new FileData
            {
                Length = length,
                Modified = modified ?? DateTime.UtcNow,
                Attributes = attributes,
                IsReparsePoint = (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint
            };
        }
    }

    public void CreateReparsePoint(string path, bool isDirectory)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            if (isDirectory)
            {
                CreateDirectory(n);
                _directories[n].IsReparsePoint = true;
                _directories[n].Attributes |= FileAttributes.ReparsePoint;
            }
            else
            {
                CreateVirtualFile(n, 0, attributes: FileAttributes.Normal | FileAttributes.ReparsePoint);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Basic Filesystem Methods
    // -------------------------------------------------------------------------

    public bool FileExists(string path)
    {
        lock (_lock) return _files.ContainsKey(Normalize(path));
    }

    public bool DirectoryExists(string path)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            return _directories.ContainsKey(n) || n == @"C:\" || n == @"D:\";
        }
    }

    public bool Exists(string path)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            return _files.ContainsKey(n) || _directories.ContainsKey(n);
        }
    }

    public void CreateDirectory(string path)
    {
        lock (_lock)
        {
            string current = Normalize(path);
            if (_files.ContainsKey(current))
            {
                throw new IOException($"Cannot create directory because a file already exists at '{path}'.");
            }

            while (!string.IsNullOrEmpty(current))
            {
                if (_files.ContainsKey(current))
                {
                    throw new IOException($"Cannot create directory because a file already exists at '{current}'.");
                }

                if (!_directories.ContainsKey(current))
                {
                    _directories[current] = new DirectoryData();
                }

                int lastSlash = current.LastIndexOf('\\');
                if (lastSlash <= 0 || (lastSlash == 2 && current[1] == ':')) break;
                current = current.Substring(0, lastSlash);
            }
        }
    }

    public void CreateFile(string path)
    {
        CreateVirtualFile(path, 0);
    }

    public void CreateFile(string path, byte[] content)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            string? dir = Path.GetDirectoryName(n);
            if (!string.IsNullOrEmpty(dir))
            {
                CreateDirectory(dir);
            }

            _files[n] = new FileData
            {
                Length = content.Length,
                SmallContent = (byte[])content.Clone(),
                Modified = DateTime.UtcNow
            };
        }
    }

    public void DeleteDirectory(string path, bool recursive = false)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            if (!_directories.ContainsKey(n))
            {
                throw new DirectoryNotFoundException($"Directory not found: '{path}'");
            }

            bool hasChildren = _files.Any(f => IsChild(n, f.Key)) || _directories.Any(d => IsChild(n, d.Key));
            if (hasChildren && !recursive)
            {
                throw new IOException($"Directory is not empty: '{path}'");
            }

            _directories.Remove(n);
            if (recursive)
            {
                var filesToRemove = _files.Keys.Where(f => IsChild(n, f)).ToList();
                foreach (var f in filesToRemove) _files.Remove(f);

                var dirsToRemove = _directories.Keys.Where(d => IsChild(n, d)).ToList();
                foreach (var d in dirsToRemove) _directories.Remove(d);
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

    public void Delete(string path)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            if (_directories.ContainsKey(n))
            {
                DeleteDirectory(n, recursive: false);
            }
            else if (_files.ContainsKey(n))
            {
                DeleteFile(n);
            }
        }
    }

    public void MoveFile(string sourcePath, string destinationPath)
    {
        Move(sourcePath, destinationPath, overwrite: false);
    }

    public void MoveDirectory(string sourcePath, string destinationPath)
    {
        Move(sourcePath, destinationPath, overwrite: false);
    }

    public void Move(string sourcePath, string destinationPath, bool overwrite = false)
    {
        lock (_lock)
        {
            string src = Normalize(sourcePath);
            string dst = Normalize(destinationPath);

            if (_files.TryGetValue(src, out var fileData))
            {
                if (_files.ContainsKey(dst))
                {
                    if (!overwrite) throw new IOException($"Destination already exists: '{destinationPath}'");
                    _files.Remove(dst);
                }
                if (_directories.ContainsKey(dst))
                {
                    throw new IOException($"Destination already exists as directory: '{destinationPath}'");
                }

                _files.Remove(src);
                _files[dst] = fileData;
                return;
            }

            if (_directories.TryGetValue(src, out var dirData))
            {
                if (overwrite)
                {
                    throw new InvalidOperationException("Directory overwrite is not supported. Use merge instead.");
                }

                if (_directories.ContainsKey(dst))
                {
                    throw new IOException($"Destination already exists: '{destinationPath}'");
                }
                if (_files.ContainsKey(dst))
                {
                    throw new IOException($"Destination already exists as file: '{destinationPath}'");
                }

                _directories.Remove(src);
                _directories[dst] = dirData;

                string srcPrefix = src + "\\";
                string dstPrefix = dst + "\\";

                var subDirs = _directories.Where(d => d.Key.StartsWith(srcPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var d in subDirs)
                {
                    _directories.Remove(d.Key);
                    _directories[dstPrefix + d.Key.Substring(srcPrefix.Length)] = d.Value;
                }

                var subFiles = _files.Where(f => f.Key.StartsWith(srcPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var f in subFiles)
                {
                    _files.Remove(f.Key);
                    _files[dstPrefix + f.Key.Substring(srcPrefix.Length)] = f.Value;
                }

                return;
            }

            throw new FileNotFoundException($"Source path not found: '{sourcePath}'");
        }
    }

    public bool IsDirectoryEmpty(string path)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            if (!_directories.ContainsKey(n)) return true;
            return !_files.Any(f => IsChild(n, f.Key)) && !_directories.Any(d => IsChild(n, d.Key));
        }
    }

    public void InjectEnumerationFailure(string path, Exception ex)
    {
        lock (_lock)
        {
            _enumerationFailures[Normalize(path)] = ex;
        }
    }

    public IEnumerable<string> EnumerateEntries(string path)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            if (_enumerationFailures.TryGetValue(n, out var ex))
            {
                throw ex;
            }
            var directDirs = _directories.Keys.Where(d => IsDirectChild(n, d));
            var directFiles = _files.Keys.Where(f => IsDirectChild(n, f));
            return directDirs.Concat(directFiles).ToList();
        }
    }

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            if (_enumerationFailures.TryGetValue(n, out var ex))
            {
                throw ex;
            }
            return _directories
                .Where(kvp => IsDirectChild(n, kvp.Key) && !kvp.Value.IsReparsePoint)
                .Select(kvp => kvp.Key)
                .ToList();
        }
    }

    public FileItemInfo? GetInfo(string path)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            if (_directories.TryGetValue(n, out var dirData))
            {
                return new FileItemInfo(true, true, 0, dirData.Modified, dirData.Attributes, dirData.IsReparsePoint);
            }
            if (_files.TryGetValue(n, out var fileData))
            {
                return new FileItemInfo(true, false, fileData.Length, fileData.Modified, fileData.Attributes, fileData.IsReparsePoint);
            }
            return null;
        }
    }

    public void SetModified(string path, DateTime time)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            DateTime utc = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
            if (_directories.TryGetValue(n, out var d))
            {
                d.Modified = utc;
            }
            else if (_files.TryGetValue(n, out var f))
            {
                f.Modified = utc;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Streams
    // -------------------------------------------------------------------------

    public Stream OpenRead(string path)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            if (!_files.TryGetValue(n, out var fileData))
            {
                throw new FileNotFoundException($"File not found: '{path}'");
            }

            return new VirtualFileStream(this, n, fileData.Length, fileData.SmallContent);
        }
    }

    public Stream CreateFileForWrite(string path)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            string? parent = Path.GetDirectoryName(n);
            if (!string.IsNullOrEmpty(parent))
            {
                CreateDirectory(parent);
            }

            return new VirtualWriteStream(this, n);
        }
    }

    public Stream OpenAppend(string path)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            string? parent = Path.GetDirectoryName(n);
            if (!string.IsNullOrEmpty(parent))
            {
                CreateDirectory(parent);
            }

            var stream = new VirtualWriteStream(this, n);
            if (_files.TryGetValue(n, out var data) && data.SmallContent != null)
            {
                stream.Write(data.SmallContent, 0, data.SmallContent.Length);
            }
            return stream;
        }
    }

    internal void CommitWrittenFile(string path, long length, byte[]? smallContent)
    {
        lock (_lock)
        {
            string n = Normalize(path);
            _files[n] = new FileData
            {
                Length = length,
                SmallContent = smallContent,
                Modified = DateTime.UtcNow
            };
        }
    }

    // -------------------------------------------------------------------------
    // Hierarchy Helpers
    // -------------------------------------------------------------------------

    private static bool IsChild(string parent, string candidate)
    {
        if (candidate.Length <= parent.Length) return false;
        string prefix = parent.EndsWith('\\') ? parent : parent + "\\";
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectChild(string parent, string candidate)
    {
        if (!IsChild(parent, candidate)) return false;
        string prefix = parent.EndsWith('\\') ? parent : parent + "\\";
        string relative = candidate.Substring(prefix.Length);
        return !relative.Contains('\\');
    }

    // -------------------------------------------------------------------------
    // Stream Implementations
    // -------------------------------------------------------------------------

    private sealed class VirtualFileStream : Stream
    {
        private readonly InMemoryFileSystem _fs;
        private readonly string _path;
        private readonly long _length;
        private readonly byte[]? _smallContent;
        private long _position;

        public VirtualFileStream(InMemoryFileSystem fs, string path, long length, byte[]? smallContent)
        {
            _fs = fs;
            _path = path;
            _length = length;
            _smallContent = smallContent;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => _position = Math.Clamp(value, 0, _length);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _length) return 0;

            int toRead = (int)Math.Min(count, _length - _position);
            _fs.CheckFailure(_path, _position, toRead);

            if (_smallContent != null && _smallContent.Length >= _length)
            {
                Array.Copy(_smallContent, _position, buffer, offset, toRead);
            }
            else
            {
                int patternOffset = (int)(_position & 0xFF);
                int written = 0;
                while (written < toRead)
                {
                    int chunk = Math.Min(toRead - written, 256 - patternOffset);
                    Array.Copy(DeterministicPattern, patternOffset, buffer, offset + written, chunk);
                    written += chunk;
                    patternOffset = 0;
                }
            }

            _position += toRead;
            return toRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => _position
            };
            Position = target;
            return Position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class VirtualWriteStream : Stream
    {
        private readonly InMemoryFileSystem _fs;
        private readonly string _path;
        private MemoryStream? _smallBuffer = new();
        private long _position;
        private long _length;
        private bool _isDisposed;

        public VirtualWriteStream(InMemoryFileSystem fs, string path)
        {
            _fs = fs;
            _path = path;
        }

        public override bool CanRead => false;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set
            {
                _position = value;
                _length = Math.Max(_length, _position);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            _fs.CheckFailure(_path, _position, count);

            if (_smallBuffer != null)
            {
                if (_position + count <= 1024 * 1024) // 1 MiB retention limit
                {
                    _smallBuffer.Position = _position;
                    _smallBuffer.Write(buffer, offset, count);
                }
                else
                {
                    _smallBuffer.Dispose();
                    _smallBuffer = null;
                }
            }

            _position += count;
            _length = Math.Max(_length, _position);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => _position
            };
            Position = target;
            return Position;
        }

        public override void Flush() { }

        public override void SetLength(long value)
        {
            _length = value;
            _smallBuffer?.SetLength(Math.Min(value, 1024 * 1024));
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                if (disposing)
                {
                    byte[]? content = _smallBuffer?.ToArray();
                    _smallBuffer?.Dispose();
                    _smallBuffer = null;
                    _fs.CommitWrittenFile(_path, _length, content);
                }
            }
            base.Dispose(disposing);
        }
    }
}
