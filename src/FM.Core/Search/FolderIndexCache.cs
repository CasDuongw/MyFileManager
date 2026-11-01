using System;
using System.Collections.Generic;
using System.IO;

namespace FM.Core.Search;

/// <summary>
/// Fast binary persistence for FolderIndex.
/// Saves and loads pre-parsed index paths to %LOCALAPPDATA%\MyFileManager\index\folders.idx.
/// Employs atomic temp-file replace and gracefully ignores corrupted or incompatible cache files.
/// </summary>
public static class FolderIndexCache
{
    private const uint Magic = 0x464D4944; // "FMID"
    private const int CurrentVersion = 1;

    public static string GetDefaultCachePath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "MyFileManager", "index", "folders.idx");
    }

    public static bool TryLoad(string filePath, out List<FolderIndexEntry> entries)
    {
        entries = new List<FolderIndexEntry>();
        if (!File.Exists(filePath)) return false;

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            uint magic = reader.ReadUInt32();
            if (magic != Magic) return false;

            int version = reader.ReadInt32();
            if (version != CurrentVersion) return false;

            int count = reader.ReadInt32();
            if (count < 0 || count > 5_000_000) return false; // Sanity check

            entries = new List<FolderIndexEntry>(count);
            for (int i = 0; i < count; i++)
            {
                string path = reader.ReadString();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    entries.Add(FolderIndexEntry.Create(path));
                }
            }

            return true;
        }
        catch
        {
            // Corrupt file or I/O error -> ignore and return false
            entries.Clear();
            return false;
        }
    }

    public static void Save(string filePath, IReadOnlyList<FolderIndexEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempPath = filePath + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(CurrentVersion);
                writer.Write(entries.Count);

                for (int i = 0; i < entries.Count; i++)
                {
                    writer.Write(entries[i].FullPath);
                }
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}
