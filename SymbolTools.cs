using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace RoFilesystem;

/// <summary>
/// Registered only when the startup probe finds a usable Universal Ctags.
/// See <see cref="CtagsStatusTools"/> for the always-available diagnostic.
/// </summary>
[McpServerToolType]
public static class SymbolTools
{
    [McpServerTool(Name = "list_symbols"), Description(
        "Map the structure of a source file: returns its symbols (functions, classes, methods, types, " +
        "constants, …) with the 1-based line number where each is defined. Use this BEFORE read_file on a " +
        "large file — read the map, then read only the line ranges you actually need with read_file's " +
        "'from' and 'to' (the line numbers here are directly usable as those arguments) instead of reading " +
        "the whole file or guessing at ranges. Works across most mainstream languages. " +
        TrustTag.ModelGuidance)]
    public static string ListSymbols(
        [Description("Absolute or root-relative path of the source file to map.")] string path)
    {
        try
        {
            var resolved = PathGuard.Resolve(path);
            if (Directory.Exists(resolved))
            {
                return $"Error: {path} is a directory — list_symbols works on a single source file.";
            }
            if (!File.Exists(resolved))
            {
                return $"Error: file not found: {path}";
            }
            if (BinaryFileDetector.IsBinary(resolved, out var kind))
            {
                return BinaryFileDetector.BinaryFileMessage(resolved, new FileInfo(resolved).Length, kind);
            }

            var tags = Ctags.ListTags(resolved);
            if (tags.Count == 0)
            {
                return $"No symbols found in {resolved}. The language may be unsupported by ctags, " +
                       "or the file may have no top-level definitions — read_file still works.";
            }

            var body = new StringBuilder();
            foreach (var tag in tags)
            {
                var qualified = string.IsNullOrEmpty(tag.Scope) ? tag.Name : $"{tag.Scope}.{tag.Name}";
                body.Append(tag.Line).Append(": ").Append(tag.Kind).Append(' ').Append(qualified).Append('\n');
            }

            return TrustTag.Wrap("symbols", resolved, body.ToString().TrimEnd('\n'));
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}

/// <summary>
/// Always registered, whatever state ctags is in, so "why can't I list symbols?"
/// has a direct answer that does not require reading the server's stderr log.
/// </summary>
[McpServerToolType]
public static class CtagsStatusTools
{
    [McpServerTool(Name = "check_ctags_status"), Description(
        "Diagnose whether the list_symbols tool is available. Reports whether Universal Ctags was found " +
        "when this server started, what version was detected, and — if it is missing or the wrong build — " +
        "the exact install command for this operating system. Call this when list_symbols is not listed " +
        "among the available tools, or when the user asks why symbol listing is not working.")]
    public static string CheckCtagsStatus()
    {
        var probe = Ctags.Status;
        var report = new StringBuilder();
        report.Append("ctags found on PATH: ").Append(probe.Found ? "yes" : "no").Append('\n');
        if (probe.Found)
        {
            report.Append("detected version: ").Append(probe.VersionLine).Append('\n');
            report.Append("is Universal Ctags: ").Append(probe.IsUniversal ? "yes" : "no").Append('\n');
        }
        else
        {
            report.Append("probe result: ").Append(probe.VersionLine).Append('\n');
        }
        report.Append("list_symbols registered: ").Append(probe.IsUsable ? "yes" : "no").Append('\n');
        report.Append("(status reflects the probe run when this server process started)\n");

        if (!probe.IsUsable)
        {
            report.Append('\n');
            if (probe.Found && !probe.IsUniversal)
            {
                report.Append(
                    "A 'ctags' command exists but is not Universal Ctags — most likely the older Exuberant " +
                    "or BSD ctags, which does not support the JSON output list_symbols needs.\n\n");
            }
            report.Append(Ctags.InstallHint());
        }

        return report.ToString();
    }
}
