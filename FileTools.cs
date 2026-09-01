using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace RoFilesystem;

[McpServerToolType]
public static class FileTools
{
    private const int DefaultGrepResults = 50;

    [McpServerTool(Name = "list_directory"), Description(
        "List the entries of a directory inside the allowed roots. Each entry is prefixed " +
        "with [FILE] or [DIR]. Use this to explore the structure of a codebase before reading files. " +
        TrustTag.ModelGuidance)]
    public static string ListDirectory(
        [Description("Absolute or root-relative path of the directory to list.")] string path)
    {
        try
        {
            var resolved = PathGuard.Resolve(path);
            if (!Directory.Exists(resolved))
                return $"Error: directory not found: {path}";

            var entries = new DirectoryInfo(resolved)
                .EnumerateFileSystemInfos()
                .OrderBy(e => e is FileInfo)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => e is DirectoryInfo ? $"[DIR]  {e.Name}" : $"[FILE] {e.Name}")
                .ToList();

            return entries.Count == 0
                ? $"(empty directory) {resolved}"
                : TrustTag.Wrap("directory_listing", resolved, string.Join('\n', entries));
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "read_file"), Description(
        "Read the contents of a text file inside the allowed roots, optionally limited to a " +
        "1-based inclusive line range via 'from' and 'to'. " +
        "Binary files (compiled assemblies, images, archives, media, etc.) CANNOT be read: this tool " +
        "returns a [BINARY FILE — not readable] notice with the file's size and likely kind instead of contents. " +
        "When you encounter one: (a) infer what the file likely is from its name, extension, and location in the " +
        "directory structure using general software-development knowledge; (b) tell the user explicitly that you " +
        "are inferring rather than reading; and (c) if you intend to look up external documentation or source for " +
        "a known library/DLL, say so before doing it. " +
        TrustTag.ModelGuidance)]
    public static string ReadFile(
        [Description("Absolute or root-relative path of the file to read.")] string path,
        [Description("Optional first line to read (1-based, inclusive).")] int? from = null,
        [Description("Optional last line to read (1-based, inclusive).")] int? to = null)
    {
        try
        {
            return ReadSingleFile(path, from, to);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "read_multiple_files"), Description(
        "Read several text files in one call. Returns each file's contents labeled by path; a failure " +
        "on one file (missing, denied, binary) is reported inline and does not fail the rest of the batch. " +
        "Binary files follow the same rules as read_file: a [BINARY FILE — not readable] notice is returned " +
        "instead of contents. " +
        TrustTag.ModelGuidance)]
    public static string ReadMultipleFiles(
        [Description("Paths of the files to read.")] string[] paths)
    {
        var results = new StringBuilder();
        foreach (var path in paths)
        {
            try
            {
                // Each file carries its own trust tag naming its path, so the batch
                // needs no separator of its own to keep the sources distinguishable.
                results.Append(ReadSingleFile(path, from: null, to: null));
            }
            catch (Exception ex)
            {
                results.Append($"{path}: Error: {ex.Message}");
            }
            results.Append("\n\n");
        }
        return results.ToString();
    }

    [McpServerTool(Name = "search_files"), Description(
        "Recursively search a directory for files and directories whose names contain the given pattern " +
        "(case-insensitive substring match). Returns full paths. Use excludePatterns to skip noise like " +
        "build output (e.g. [\"bin\", \"obj\", \"node_modules\", \".git\"]); an exclude pattern matches when " +
        "any path segment equals it or the entry name contains it. " +
        TrustTag.ModelGuidance)]
    public static string SearchFiles(
        [Description("Directory to search from (searched recursively).")] string path,
        [Description("Case-insensitive substring to match against file and directory names.")] string pattern,
        [Description("Optional name fragments to exclude from the search.")] string[]? excludePatterns = null)
    {
        try
        {
            var resolved = PathGuard.Resolve(path);
            if (!Directory.Exists(resolved))
            {
                return $"Error: directory not found: {path}";
            }

            var matches = DirectoryWalk.Enumerate(new DirectoryInfo(resolved), excludePatterns ?? [])
                .Where(e => e.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.FullName)
                .ToList();

            return matches.Count == 0
                ? $"No matches for '{pattern}' under {resolved}"
                : TrustTag.Wrap("search_results", resolved, string.Join('\n', matches));
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "grep"), Description(
        "Recursively search file CONTENTS under a directory and return each match as " +
        "path:line:text. This is the tool for 'where is X used/defined/mentioned' questions — " +
        "search_files only matches file and directory NAMES, not what is inside them. " +
        "By default 'pattern' is a plain literal substring (case-insensitive); set isRegex=true to " +
        "treat it as a .NET regular expression. Regex mode runs on a linear-time engine that does not " +
        "support lookarounds, backreferences, or atomic groups — those return an error rather than " +
        "running slowly. Binary files are skipped. Results are capped at maxResults (default 50); if you " +
        "hit the cap, narrow the pattern or the search directory rather than raising it blindly. " +
        TrustTag.ModelGuidance)]
    public static string Grep(
        [Description("Directory to search from (searched recursively), or a single file.")] string path,
        [Description("Literal substring to find, or a regular expression when isRegex is true.")] string pattern,
        [Description("Optional name fragments whose files and directories are skipped, e.g. [\"bin\", \"obj\", \"node_modules\", \".git\"].")] string[]? excludePatterns = null,
        [Description("Maximum number of matching lines to return. Defaults to 50.")] int? maxResults = null,
        [Description("Treat 'pattern' as a regular expression instead of a literal substring. Defaults to false.")] bool isRegex = false)
    {
        try
        {
            var resolved = PathGuard.Resolve(path);
            if (!Directory.Exists(resolved) && !File.Exists(resolved))
            {
                return $"Error: no such file or directory: {path}";
            }
            if (string.IsNullOrEmpty(pattern))
            {
                return "Error: pattern must not be empty.";
            }

            var limit = Math.Max(maxResults ?? DefaultGrepResults, 1);

            Regex? regex = null;
            if (isRegex)
            {
                try
                {
                    // NonBacktracking is a DFA-based engine: linear in input length, so a
                    // pathological pattern cannot blow up the search. The timeout is a
                    // secondary net for very large inputs.
                    regex = new Regex(pattern, RegexOptions.NonBacktracking, TimeSpan.FromSeconds(5));
                }
                catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
                {
                    return $"Error: {DescribeUnsupportedPattern(pattern)} Details: {ex.Message}";
                }
            }

            var files = Directory.Exists(resolved)
                ? DirectoryWalk.Enumerate(new DirectoryInfo(resolved), excludePatterns ?? []).OfType<FileInfo>()
                : [new FileInfo(resolved)];

            var matches = new List<string>();
            var truncated = false;
            foreach (var file in files)
            {
                if (matches.Count >= limit)
                {
                    truncated = true;
                    break;
                }
                if (BinaryFileDetector.IsBinary(file.FullName, out _))
                {
                    continue;
                }

                var lineNumber = 0;
                IEnumerable<string> lines;
                try
                {
                    lines = File.ReadLines(file.FullName);
                }
                catch (Exception)
                {
                    continue; // unreadable file — skip, don't fail the whole search
                }

                foreach (var line in lines)
                {
                    lineNumber++;
                    var hit = regex is null
                        ? line.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                        : regex.IsMatch(line);
                    if (!hit)
                    {
                        continue;
                    }
                    if (matches.Count >= limit)
                    {
                        truncated = true;
                        break;
                    }
                    matches.Add($"{file.FullName}:{lineNumber}:{line}");
                }
            }

            if (matches.Count == 0)
            {
                return $"No content matches for '{pattern}' under {resolved}";
            }

            var body = string.Join('\n', matches);
            if (truncated)
            {
                body += $"\n\n[truncated at {limit} matches — narrow the pattern or search directory for more]";
            }
            return TrustTag.Wrap("grep_results", resolved, body);
        }
        catch (RegexMatchTimeoutException)
        {
            return "Error: the search timed out after 5 seconds. Try a simpler pattern or a narrower directory.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Names the regex feature the linear-time engine rejected, so the caller gets an
    /// actionable message instead of only the raw engine exception.
    /// </summary>
    private static string DescribeUnsupportedPattern(string pattern)
    {
        if (Regex.IsMatch(pattern, @"\(\?(=|!|<=|<!)"))
            return "This pattern uses a lookaround ((?=…), (?!…), (?<=…), (?<!…)), which the linear-time regex engine does not support. Rewrite the pattern without it, or use a literal substring search (isRegex=false).";
        if (Regex.IsMatch(pattern, @"\\[1-9]"))
            return "This pattern uses a backreference (\\1, \\2, …), which the linear-time regex engine does not support. Rewrite the pattern without it, or use a literal substring search (isRegex=false).";
        if (pattern.Contains("(?>"))
            return "This pattern uses an atomic group ((?>…)), which the linear-time regex engine does not support. Rewrite the pattern without it, or use a literal substring search (isRegex=false).";
        return "This pattern could not be compiled by the linear-time regex engine.";
    }

    [McpServerTool(Name = "get_file_info"), Description(
        "Get metadata for a file or directory inside the allowed roots: size, created/modified/accessed " +
        "timestamps, whether it is a file or directory, and Unix permissions. Does not read contents, so " +
        "it works on binary files too. " +
        TrustTag.ModelGuidance)]
    public static string GetFileInfo(
        [Description("Absolute or root-relative path of the file or directory.")] string path)
    {
        try
        {
            var resolved = PathGuard.Resolve(path);
            FileSystemInfo info = Directory.Exists(resolved)
                ? new DirectoryInfo(resolved)
                : new FileInfo(resolved);
            if (!info.Exists)
            {
                return $"Error: no such file or directory: {path}";
            }

            var isDir = info is DirectoryInfo;
            var size = isDir ? "-" : BinaryFileDetector.FormatSize(((FileInfo)info).Length);
            return TrustTag.Wrap("file_info", resolved, $"""
                path: {resolved}
                type: {(isDir ? "directory" : "file")}
                size: {size}
                created: {info.CreationTimeUtc:yyyy-MM-dd HH:mm:ss}Z
                modified: {info.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss}Z
                accessed: {info.LastAccessTimeUtc:yyyy-MM-dd HH:mm:ss}Z
                permissions: {info.UnixFileMode}
                """);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "list_allowed_directories"), Description(
        "List the root directories this server is allowed to read from. Every path passed to the other " +
        "tools must resolve to a location inside one of these roots.")]
    public static string ListAllowedDirectories() =>
        string.Join('\n', PathGuard.AllowedRoots);

    private static string ReadSingleFile(string path, int? from, int? to)
    {
        var resolved = PathGuard.Resolve(path);
        if (Directory.Exists(resolved))
        {
            return $"Error: {path} is a directory — use list_directory instead.";
        }
        if (!File.Exists(resolved))
        {
            return $"Error: file not found: {path}";
        }

        var size = new FileInfo(resolved).Length;
        if (BinaryFileDetector.IsBinary(resolved, out var kind))
        {
            return BinaryFileDetector.BinaryFileMessage(resolved, size, kind);
        }

        if (from is null && to is null)
        {
            return TrustTag.Wrap("file_content", resolved, File.ReadAllText(resolved));
        }

        var start = Math.Max(from ?? 1, 1);
        var lines = File.ReadLines(resolved)
            .Skip(start - 1)
            .Take(to is int end ? Math.Max(end - start + 1, 0) : int.MaxValue)
            .ToList();
        return lines.Count == 0
            ? $"(no lines in range {from}-{to}) {resolved}"
            : TrustTag.Wrap("file_content", resolved, string.Join('\n', lines));
    }
}
