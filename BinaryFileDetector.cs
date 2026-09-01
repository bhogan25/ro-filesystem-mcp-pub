namespace RoFilesystem;

/// <summary>
/// Two-layer binary detection: a known-extension denylist (with a best-guess
/// description of the file kind), plus a content sniff that treats a null byte
/// in the first 1KB as binary.
/// </summary>
public static class BinaryFileDetector
{
    private static readonly Dictionary<string, string> KnownBinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".dll"] = "a compiled .NET assembly or native library",
        [".exe"] = "a compiled executable",
        [".so"] = "a native shared library (Linux)",
        [".dylib"] = "a native shared library (macOS)",
        [".png"] = "a PNG image",
        [".jpg"] = "a JPEG image",
        [".jpeg"] = "a JPEG image",
        [".gif"] = "a GIF image",
        [".ico"] = "an icon image",
        [".webp"] = "a WebP image",
        [".pdf"] = "a PDF document",
        [".zip"] = "a ZIP archive",
        [".tar"] = "a tar archive",
        [".gz"] = "a gzip-compressed file",
        [".7z"] = "a 7-Zip archive",
        [".mp3"] = "an MP3 audio file",
        [".mp4"] = "an MP4 video file",
        [".wav"] = "a WAV audio file",
        [".woff"] = "a web font file",
        [".woff2"] = "a web font file",
        [".ttf"] = "a TrueType font file",
        [".pdb"] = "a debug symbols file",
        [".bin"] = "a binary data file",
        [".class"] = "compiled Java bytecode",
        [".pyc"] = "compiled Python bytecode",
        [".o"] = "a compiled object file",
        [".a"] = "a static library archive",
    };

    /// <summary>
    /// Returns true when the file at <paramref name="path"/> should be treated as
    /// binary; <paramref name="kind"/> carries a best-guess human description.
    /// The path must already be validated by <see cref="PathGuard"/>.
    /// </summary>
    public static bool IsBinary(string path, out string kind)
    {
        if (KnownBinaryExtensions.TryGetValue(Path.GetExtension(path), out var known))
        {
            kind = known;
            return true;
        }

        using var stream = File.OpenRead(path);
        Span<byte> buffer = stackalloc byte[1024];
        var read = stream.Read(buffer);
        if (buffer[..read].Contains((byte)0))
        {
            kind = "an unrecognized binary format (contains null bytes)";
            return true;
        }

        kind = string.Empty;
        return false;
    }

    /// <summary>Formats the structured refusal message returned in place of binary file contents.</summary>
    public static string BinaryFileMessage(string path, long sizeBytes, string kind) =>
        $"[BINARY FILE — not readable] {path} (size: {FormatSize(sizeBytes)}). This appears to be {kind}.";

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB",
    };
}
