using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RoFilesystem;

if (args.Length == 0)
{
    Console.Error.WriteLine("Error: no allowed directories provided.");
    Console.Error.WriteLine("Usage: ro-filesystem <allowed-directory> [<allowed-directory> ...]");
    return 1;
}

try
{
    PathGuard.Initialize(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

// Probed once per process. list_symbols is registered only when a usable Universal Ctags
// is present, so it is never callable-but-broken; installing ctags then restarting Claude
// Desktop re-runs this and makes the tool appear.
var ctags = Ctags.Detect();
List<Type> toolTypes = [typeof(FileTools), typeof(CtagsStatusTools)];
if (ctags.IsUsable)
{
    toolTypes.Add(typeof(SymbolTools));
}
else
{
    Console.Error.WriteLine(ctags.Found
        ? $"Notice: 'ctags' was found but is not Universal Ctags (detected: {ctags.VersionLine})."
        : "Notice: Universal Ctags was not found on PATH.");
    Console.Error.WriteLine("The list_symbols tool is not registered for this session.");
    Console.Error.WriteLine(Ctags.InstallHint());
    Console.Error.WriteLine("Call the check_ctags_status tool at any time to re-read this diagnosis.");
}

var builder = Host.CreateEmptyApplicationBuilder(settings: null);

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    // The IEnumerable<Type> cast is load-bearing: without it a List<Type> binds to the
    // generic WithTools<T>(T instance) overload, which registers the list itself as a
    // tool object and silently leaves the server with no tools at all.
    .WithTools((IEnumerable<Type>)toolTypes);

await builder.Build().RunAsync();
return 0;
