namespace RoFilesystem;

/// <summary>
/// The security boundary of this server: every tool that accepts a path must go
/// through <see cref="Resolve"/>, which canonicalizes the path (including symlinks
/// in any segment) and rejects anything outside the allowed roots.
/// </summary>
public static class PathGuard
{
    private static string[] _roots = [];

    public static IReadOnlyList<string> AllowedRoots => _roots;

    /// <summary>Resolve and validate the allowed roots once at startup. Fails fast on missing directories.</summary>
    public static void Initialize(IEnumerable<string> roots)
    {
        var resolved = new List<string>();
        foreach (var root in roots)
        {
            var fullPath = Path.GetFullPath(root);
            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException($"Allowed root is not an existing directory: {fullPath}");
            }
            resolved.Add(Canonicalize(fullPath));
        }
        if (resolved.Count == 0)
        {
            throw new ArgumentException("At least one allowed directory is required.");
        }
        _roots = resolved.ToArray();
    }

    /// <summary>
    /// Returns the canonical absolute path for <paramref name="path"/>, or throws
    /// <see cref="UnauthorizedAccessException"/> if it escapes every allowed root
    /// (via relative segments, absolute paths elsewhere, or symlinks).
    /// </summary>
    public static string Resolve(string path)
    {
        var canonical = Canonicalize(Path.GetFullPath(path));
        if (!IsWithinAllowedRoot(canonical))
        {
            throw new UnauthorizedAccessException(
                $"Access denied: '{path}' resolves to '{canonical}', which is outside the allowed directories " +
                $"({string.Join(", ", _roots)}).");
        }
        return canonical;
    }

    private static bool IsWithinAllowedRoot(string canonicalPath) =>
        _roots.Any(root =>
            canonicalPath.Equals(root, StringComparison.Ordinal) ||
            canonicalPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    /// <summary>
    /// Resolves the path segment by segment so a symlink anywhere in the path
    /// (not just the final component) cannot smuggle the real location outside
    /// an allowed root. Non-existent trailing segments are appended lexically —
    /// reads on them fail with file-not-found later anyway.
    /// </summary>
    private static string Canonicalize(string fullPath)
    {
        var current = Path.GetPathRoot(fullPath)!;
        var segments = fullPath[current.Length..]
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < segments.Length; i++)
        {
            current = Path.Join(current, segments[i]);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                // Remainder doesn't exist; nothing left to resolve.
                for (var j = i + 1; j < segments.Length; j++)
                    current = Path.Join(current, segments[j]);
                break;
            }
            var target = File.ResolveLinkTarget(current, returnFinalTarget: true);
            if (target is not null)
            {
                current = target.FullName;
            }
        }
        return current;
    }
}
