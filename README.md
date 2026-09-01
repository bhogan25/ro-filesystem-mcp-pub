# ro-filesystem

A read-only filesystem MCP server that gives a conversational agent direct access to a codebase on your disk. No write, edit, or delete tool exists anywhere in it.

## Why this exists

This is for anyone who wants to explore and discuss a codebase — get up to speed on an unfamiliar project, understand how something works, or just talk through code with an agent instead of reading it alone. A standard chat interface can't see your local files at all; the usual workaround is pasting code in by hand, snippet by snippet, losing context every time you need something else. This server gives a conversational agent direct access to a real project on disk instead.

Another option for reading files is with coding agents, which are great for focused write/edit work, but they're a clunky fit for open-ended "walk me through this codebase" conversations. A desktop chat app — drag-and-drop files, other connectors, natural back-and-forth — is a better place for that kind of exploration, especially for private repos sitting on your disk that you can't just point a chatbot at otherwise. This server bridges that gap: fast, read-only access to real code, no publishing required.

No write, edit, or delete tool is implemented anywhere in this server — that's a design choice, not a config flag. Every tool that returns file content also wraps it with an explicit untrusted-data marker (see `SECURITY.md`): the server doesn't try to detect or filter anything, it just labels where content came from so the calling agent knows not to treat file contents as instructions.

## Tools

| Tool | What it does |
| --- | --- |
| `list_directory` | Lists a directory's entries, each marked `[FILE]` or `[DIR]`. |
| `read_file` | Reads a text file, optionally just a 1-based inclusive line range. |
| `read_multiple_files` | Reads several files in one call; one failure doesn't sink the batch. |
| `search_files` | Recursively searches file and directory **names** (case-insensitive substring). |
| `grep` | Recursively searches file **contents**, returning `path:line:text`. |
| `list_symbols` | Maps a source file's functions, classes, and methods to line numbers. Requires Universal Ctags. |
| `get_file_info` | Size, timestamps, file-vs-directory, and permissions. Works on binary files. |
| `check_ctags_status` | Reports whether `list_symbols` is available, and how to fix it if not. |
| `list_allowed_directories` | Lists the roots this server may read from. |

Two behaviors worth knowing:

- **Binary files are never returned as content.** `read_file` detects them by extension and by sniffing for null bytes, and returns a `[BINARY FILE — not readable]` notice with the size and likely file kind instead.
- **`list_symbols` line numbers are directly usable** as `read_file`'s `from` and `to`. On a large file, map it first and read only the ranges you need rather than pulling the whole thing into context.

## A note on prompt injection

This server hands whatever it reads straight to the connected agent. Every tool tags returned content as untrusted data (see `SECURITY.md`), which is meant to stop the agent from mistaking something inside a file for an instruction — but that's a mitigation, not a guarantee. A file engineered to manipulate an agent, or an agent that's careless about the distinction, could still cause something unintended to happen.

Be especially mindful with repos you didn't write yourself, or don't fully trust. Pay attention to what the agent actually does after reading a file, and don't treat this server as a reason to stop paying attention.

## Requirements

- **.NET 10 SDK** (the project targets `net10.0`)
- **Universal Ctags** — optional, and only needed for `list_symbols`

## Installation

```bash
git clone <this-repo> ro-filesystem
cd ro-filesystem
dotnet build
```

### Universal Ctags (optional)

`list_symbols` shells out to [Universal Ctags](https://github.com/universal-ctags/ctags). Without it, every other tool works normally and `list_symbols` simply isn't registered — it won't appear as a callable tool rather than appearing and failing when used.

```bash
sudo apt install universal-ctags     # Debian/Ubuntu
sudo dnf install ctags               # Fedora/RHEL
brew install universal-ctags         # macOS
winget install UniversalCtags.Ctags  # Windows
```

Note that some systems ship an older Exuberant or BSD `ctags` under the same name; it lacks the JSON output this server needs, and won't be accepted. The server checks for the string `Universal Ctags` in `ctags --version`, not merely that a `ctags` command exists.

After installing, **fully restart Claude Desktop**. The check runs once when the server process starts, so a restart is what makes `list_symbols` appear. If you're unsure of the current state, ask the agent to call `check_ctags_status` — it reports what was detected and the exact install command for your OS.

## Usage

The server takes one or more allowed root directories as command-line arguments. Those roots are the entire security boundary: every path argument to every tool must resolve inside one of them. With no arguments, the server exits immediately with an error.

Add it to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "ro-filesystem": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/absolute/path/to/ro-filesystem",
        "--no-build",
        "--",
        "/absolute/path/to/your/repo"
      ]
    }
  }
}
```

- The `--` separator is required, so the directory arguments reach the server rather than `dotnet run`.
- `--no-build` means Claude Desktop runs the already-compiled output — run `dotnet build` first.
- To grant access to more repos, append their paths as additional arguments after the first, then restart Claude Desktop.

## Security

See [`SECURITY.md`](SECURITY.md) for the read-only guarantee, the path-containment model, and how untrusted-content tagging works.

## License

MIT — see [`LICENSE`](LICENSE).
