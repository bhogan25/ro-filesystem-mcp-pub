using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace RoFilesystem;

/// <summary>
/// Detection of and interaction with Universal Ctags, which backs <c>list_symbols</c>.
/// The probe runs once at startup and decides whether <c>list_symbols</c> is registered
/// at all — an unavailable tool should not be callable, rather than callable and broken.
/// </summary>
public static class Ctags
{
    /// <summary>Outcome of the startup probe. Reported verbatim by <c>check_ctags_status</c>.</summary>
    public sealed record Probe(bool Found, bool IsUniversal, string VersionLine)
    {
        public bool IsUsable => Found && IsUniversal;
    }

    private static Probe _probe = new(Found: false, IsUniversal: false, VersionLine: "(not probed)");

    public static Probe Status => _probe;

    /// <summary>
    /// Runs the startup probe. `ctags` existing on PATH is not enough: several systems ship an
    /// older Exuberant/BSD ctags under the same name, which does not speak --output-format=json.
    /// </summary>
    public static Probe Detect()
    {
        try
        {
            var (exitCode, stdout, stderr) = Run("--version", TimeSpan.FromSeconds(10));
            var output = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
            var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
                ?? $"(no output, exit code {exitCode})";
            _probe = new Probe(
                Found: true,
                IsUniversal: output.Contains("Universal Ctags", StringComparison.OrdinalIgnoreCase),
                VersionLine: firstLine);
        }
        catch (Exception ex)
        {
            _probe = new Probe(Found: false, IsUniversal: false, VersionLine: ex.Message);
        }
        return _probe;
    }

    /// <summary>Lists the tags in one file as JSON Lines records, ordered by line number.</summary>
    public static IReadOnlyList<Tag> ListTags(string resolvedPath)
    {
        var (exitCode, stdout, stderr) = Run(
            $"--output-format=json --fields=+n -o - \"{resolvedPath}\"", TimeSpan.FromSeconds(30));
        if (exitCode != 0 && stdout.Length == 0)
        {
            throw new InvalidOperationException(
                $"ctags exited with code {exitCode}: {stderr.Trim()}");
        }

        var tags = new List<Tag>();
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            JsonElement record;
            try
            {
                record = JsonSerializer.Deserialize<JsonElement>(line);
            }
            catch (JsonException)
            {
                continue; // ctags occasionally emits a non-record line; skip it rather than fail
            }
            if (record.ValueKind != JsonValueKind.Object ||
                !record.TryGetProperty("_type", out var type) || type.GetString() != "tag")
            {
                continue;
            }

            tags.Add(new Tag(
                Name: Text(record, "name"),
                Kind: Text(record, "kind"),
                Scope: Text(record, "scope"),
                Line: record.TryGetProperty("line", out var l) && l.TryGetInt32(out var n) ? n : 0));
        }

        return tags.OrderBy(t => t.Line).ToList();

        static string Text(JsonElement record, string property) =>
            record.TryGetProperty(property, out var value) ? value.GetString() ?? "" : "";
    }

    public sealed record Tag(string Name, string Kind, string Scope, int Line);

    /// <summary>The install command for the current OS, plus the restart step that makes the tool appear.</summary>
    public static string InstallHint()
    {
        var install = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? "brew install universal-ctags"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "winget install UniversalCtags.Ctags   (or: choco install universal-ctags)"
                : LinuxInstallCommand();

        return $"""
            To enable list_symbols, install Universal Ctags:
                {install}
            Then fully restart Claude Desktop (which restarts this server), and list_symbols
            becomes available on the next connection. Nothing is broken — the tool is simply
            not registered while its dependency is missing.
            """;
    }

    private static string LinuxInstallCommand()
    {
        var release = ReadOsRelease();
        if (release.Contains("debian") || release.Contains("ubuntu"))
            return "sudo apt install universal-ctags";
        if (release.Contains("fedora") || release.Contains("rhel") || release.Contains("centos"))
            return "sudo dnf install ctags";
        if (release.Contains("arch"))
            return "sudo pacman -S ctags";
        if (release.Contains("suse"))
            return "sudo zypper install ctags";
        return "install the 'universal-ctags' package with your distribution's package manager";
    }

    private static string ReadOsRelease()
    {
        try
        {
            return File.Exists("/etc/os-release")
                ? File.ReadAllText("/etc/os-release").ToLowerInvariant()
                : "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(string arguments, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("ctags", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();
        // Read both streams before waiting: a full pipe buffer would otherwise deadlock us.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { /* already gone */ }
            throw new TimeoutException($"ctags did not finish within {timeout.TotalSeconds:0} seconds.");
        }
        return (process.ExitCode, stdout.Result, stderr.Result);
    }
}
