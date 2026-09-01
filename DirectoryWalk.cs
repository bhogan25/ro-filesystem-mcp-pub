namespace RoFilesystem;

/// <summary>
/// Shared recursive enumeration for the tools that search a subtree, so they agree
/// on how excludes are applied and on never following directory symlinks (whose
/// targets may lie outside the allowed roots).
/// </summary>
public static class DirectoryWalk
{
    /// <summary>
    /// Yields every entry beneath <paramref name="root"/>, skipping entries whose name
    /// contains any of <paramref name="excludes"/> (case-insensitive) and pruning
    /// excluded directories entirely. Unreadable directories are skipped rather than
    /// failing the whole walk.
    /// </summary>
    public static IEnumerable<FileSystemInfo> Enumerate(DirectoryInfo root, string[] excludes)
    {
        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = root.EnumerateFileSystemInfos();
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            if (excludes.Any(x => entry.Name.Contains(x, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            yield return entry;

            if (entry is DirectoryInfo sub && entry.LinkTarget is null)
            {
                foreach (var nested in Enumerate(sub, excludes))
                {
                    yield return nested;
                }
            }
        }
    }
}
