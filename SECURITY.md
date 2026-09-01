# Security

This server is designed to be pointed at a real codebase on your disk and handed to an agent. Three properties make that reasonable, and each has limits worth stating plainly.

## 1. Read-only, structurally

No write, edit, move, create, or delete tool is implemented anywhere in this codebase. This is not a mode, a flag, or a permission check that could be misconfigured — the code to modify a file does not exist, so no sequence of tool calls can reach it.

The practical consequence: an agent connected to this server cannot change your files, no matter what it is asked to do or what it reads. The worst case is disclosure of file contents the server was pointed at, not modification of them.

## 2. Path containment

The allowed root directories passed on the command line are the entire security boundary. Every tool that accepts a path routes it through `PathGuard.Resolve`, which:

- resolves the path to an absolute canonical form,
- walks it **segment by segment**, resolving symlinks at every level rather than only the final component, so a symlinked intermediate directory cannot smuggle the real target outside a root,
- requires the result to equal an allowed root or begin with that root plus a directory separator — the separator check is what stops `/home/you/Code-other` from passing for the root `/home/you/Code`,
- and throws otherwise.

Recursive tools (`search_files`, `grep`) additionally never follow directory symlinks, since a symlink inside an allowed root may point outside it.

If no directory is passed on the command line, the server refuses to start. There is no implicit default root.

## 3. Untrusted-content tagging

Everything this server returns from disk is wrapped in an explicit provenance marker:

```
<file_content_a7f3e91c4b02 path="src/foo.py" trust="untrusted_external_data">
...raw, completely unmodified file content...
</file_content_a7f3e91c4b02>
```

The random suffix on the tag name is generated fresh for every wrapper the server emits, and only a closing tag carrying that exact suffix ends the untrusted region. See "Tag breakout" below for what it defends against.

The same treatment applies to directory listings, search results, grep results, symbol listings, and file metadata — filenames included, since a filename can carry injected text as easily as a file body can. `list_allowed_directories` is the one exception: it returns server configuration, not repo content.

**The server does not inspect, score, filter, or sanitize anything.** There is no blocklist of suspicious phrases and no heuristic detection. That approach produces false positives on legitimate code and documentation, and fails against any attacker who rephrases. Instead the server does the one thing it can do reliably: state where the content came from, and preserve the content exactly.

This is deliberately a two-part defense:

1. **The server labels.** Mechanical, uniform, and content-blind.
2. **The calling model is expected to honor the label.** Each tool's description tells the model that returned content is untrusted external data and that instructions found inside it must never be followed, only reported on.

Neither half is sufficient alone, and together they are a mitigation rather than a guarantee. A sufficiently well-crafted file, or a model that is careless about the boundary, can still cause unintended behavior. Since the server is read-only, "unintended behavior" is bounded by what the agent itself can do — but if that agent has other tools available, that bound may be much wider than this server's own.

**Treat repos you did not write as untrusted input, and watch what the agent does after reading from them.**

### Tag breakout

Because file content is preserved byte-exactly and never modified, a file whose contents include a literal closing tag such as `</file_content>` could once visually break out of its wrapper, making the text after it appear to sit outside the untrusted-data marker. Escaping the sequence was rejected: altering content would undermine the more important property that what you see is exactly what is on disk.

The per-wrapper random suffix addresses this without touching content. Whoever authored a file had to commit to its bytes before the suffix was generated, so they cannot embed a closing tag that matches — a planted `</file_content>` stays inert text inside the region. The suffix is cryptographically random and regenerated per wrapper, so it cannot be predicted from the source, from a previous response, or from another wrapper in the same response.

This narrows the hole rather than proving it closed. The protection is behavioral: it works because the reading model treats only the suffix-matched closing tag as authoritative, which nothing in the server enforces. That is why the tagging above is still described as a mitigation rather than a guarantee.

## Support status

This is a personal project published as-is, under the MIT license. It is not actively maintained: security reports and other issues are not monitored, and no fixes, updates, or responses should be expected.

The properties described above are documented so you can evaluate the code yourself and decide whether it meets your needs. If you depend on this, review it and fork it — treat the version you run as yours to maintain.
