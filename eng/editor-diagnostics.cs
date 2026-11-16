// The editor check of M1a without the editor window (docs/evidence/m1a-editor-diagnostics.md): drives the Roslyn
// language server that the VS Code C# extension runs, as the editor does. It opens the Notebook project and its
// contract file, changes the file in memory as typing would (nothing is saved), asks for the document's diagnostics
// until SPAR015 and SPAR024 appear, then undoes the change and waits for them to clear. Copy it out of the repository
// and run it there, or the repository's build settings apply to it:
//   dotnet run editor-diagnostics.cs -- <extension>\.roslyn\Microsoft.CodeAnalysis.LanguageServer.exe
//       <repo>\samples\Notebook\Notebook.csproj <repo>\samples\Notebook\Pages\PagesService.cs <log folder>
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var (server, project, file, logs) = (args[0], args[1], args[2], args[3]);
var start = new ProcessStartInfo(server)
{
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
};
foreach (var a in new[] { "--stdio", "--logLevel", "Information", "--extensionLogDirectory", logs })
{
    start.ArgumentList.Add(a);
}

using var process = Process.Start(start)!;
process.ErrorDataReceived += (_, e) => { };
process.BeginErrorReadLine();
var output = process.StandardOutput.BaseStream;
var input = process.StandardInput.BaseStream;
var nextId = 0;
var pending = new Dictionary<int, TaskCompletionSource<JsonNode?>>();
var initialized = new TaskCompletionSource();
var registrations = new List<string?>();
var clock = Stopwatch.StartNew();
void Log(string text) => Console.WriteLine($"{clock.Elapsed.TotalSeconds,7:0.00}s {text}");

Task Send(JsonObject message)
{
    message["jsonrpc"] = "2.0";
    var body = Encoding.UTF8.GetBytes(message.ToJsonString());
    var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
    lock (input)
    {
        input.Write(header);
        input.Write(body);
        input.Flush();
    }

    return Task.CompletedTask;
}

Task<JsonNode?> Request(string method, JsonNode? parameters)
{
    var id = Interlocked.Increment(ref nextId);
    var done = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
    lock (pending)
    {
        pending[id] = done;
    }

    _ = Send(new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters });
    return done.Task;
}

Task Notify(string method, JsonNode? parameters) => Send(new JsonObject { ["method"] = method, ["params"] = parameters });

async Task ReadLoop()
{
    var reader = new BufferedStream(output);
    while (true)
    {
        var length = 0;
        while (true)
        {
            var line = new StringBuilder();
            int b;
            while ((b = reader.ReadByte()) != '\n')
            {
                if (b < 0)
                {
                    return;
                }

                if (b != '\r')
                {
                    line.Append((char)b);
                }
            }

            if (line.Length == 0)
            {
                break;
            }

            if (line.ToString().StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line.ToString()["Content-Length:".Length..].Trim());
            }
        }

        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            read += reader.Read(body, read, length - read);
        }

        var message = JsonNode.Parse(body)!.AsObject();
        if (message.TryGetPropertyValue("method", out var methodNode))
        {
            var method = methodNode!.GetValue<string>();
            if (message.TryGetPropertyValue("id", out var requestId))
            {
                // A request from the server: answer what the editor would.
                JsonNode? result = null;
                if (method == "workspace/configuration")
                {
                    var items = message["params"]!["items"]!.AsArray();
                    result = new JsonArray(items.Select(_ => (JsonNode?)null).ToArray());
                }
                else if (method == "client/registerCapability")
                {
                    foreach (var registration in message["params"]!["registrations"]!.AsArray())
                    {
                        if (registration!["method"]!.GetValue<string>() == "textDocument/diagnostic")
                        {
                            registrations.Add(registration["registerOptions"]?["identifier"]?.GetValue<string>());
                        }
                    }
                }

                await Send(new JsonObject { ["id"] = requestId!.DeepClone(), ["result"] = result });
                continue;
            }

            if (method == "workspace/projectInitializationComplete")
            {
                initialized.TrySetResult();
            }
            else if (method == "window/logMessage" && message["params"]!["type"]!.GetValue<int>() <= 1)
            {
                Log("server error: " + message["params"]!["message"]);
            }

            continue;
        }

        var id = message["id"]!.GetValue<int>();
        TaskCompletionSource<JsonNode?> done;
        lock (pending)
        {
            done = pending[id];
        }

        if (message.TryGetPropertyValue("error", out var error))
        {
            done.SetException(new InvalidOperationException(error!.ToJsonString()));
        }
        else
        {
            done.SetResult(message["result"]?.DeepClone());
        }
    }
}

_ = Task.Run(ReadLoop);
var root = new Uri(Path.GetDirectoryName(project)!).AbsoluteUri;
var capabilities = JsonNode.Parse("""
    {
      "textDocument": { "diagnostic": { "dynamicRegistration": true }, "synchronization": { "dynamicRegistration": true } },
      "workspace": { "configuration": true, "diagnostics": { "refreshSupport": true } },
      "window": { "workDoneProgress": true }
    }
    """);
await Request("initialize", new JsonObject
{
    ["processId"] = Environment.ProcessId,
    ["rootUri"] = root,
    ["capabilities"] = capabilities,
    ["workspaceFolders"] = new JsonArray(new JsonObject { ["uri"] = root, ["name"] = "probe" }),
});
await Notify("initialized", new JsonObject());
Log("initialized");
await Notify("project/open", new JsonObject { ["projects"] = new JsonArray(new Uri(project).AbsoluteUri) });
await initialized.Task.WaitAsync(TimeSpan.FromMinutes(5));
Log("project loaded; diagnostic identifiers: " + string.Join(", ", registrations.Select(r => r ?? "(none)")));

var uri = new Uri(file).AbsoluteUri;
var original = File.ReadAllText(file);
await Notify("textDocument/didOpen", new JsonObject
{
    ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = "csharp", ["version"] = 1, ["text"] = original },
});

async Task<List<string>> Diagnostics()
{
    var found = new List<string>();
    var identifiers = registrations.Count == 0 ? new List<string?> { null } : registrations;
    foreach (var identifier in identifiers)
    {
        var parameters = new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } };
        if (identifier is not null)
        {
            parameters["identifier"] = identifier;
        }

        var report = await Request("textDocument/diagnostic", parameters);
        foreach (var item in report?["items"]?.AsArray() ?? [])
        {
            var code = item!["code"]?.ToString() ?? "";
            if (!code.StartsWith("SPAR", StringComparison.Ordinal) && item["severity"]?.GetValue<int>() != 1)
            {
                continue;
            }

            var range = item["range"]!;
            var severity = item["severity"]?.GetValue<int>() switch { 1 => "error", 2 => "warning", 3 => "info", _ => "hint" };
            found.Add($"{code} {severity} at {range["start"]!["line"]!.GetValue<int>() + 1}:{range["start"]!["character"]!.GetValue<int>() + 1}-{range["end"]!["line"]!.GetValue<int>() + 1}:{range["end"]!["character"]!.GetValue<int>() + 1} [{identifier}] {item["message"]}");
        }
    }

    return found;
}

async Task<List<string>> WaitFor(Func<List<string>, bool> done, string what)
{
    var started = clock.Elapsed;
    while (true)
    {
        var found = await Diagnostics();
        if (done(found) || clock.Elapsed - started > TimeSpan.FromSeconds(60))
        {
            Log($"{what} after {(clock.Elapsed - started).TotalSeconds:0.00}s: {found.Count} diagnostics");
            foreach (var line in found)
            {
                Console.WriteLine("    " + line);
            }

            return found;
        }

        await Task.Delay(250);
    }
}

await WaitFor(found => true, "the file as committed");

// The edit, in memory only, as typing it would be: two commands at the end of the contract.
const string Marker = "    private static string DescribeInvalid";
var broken = original.Replace(Marker,
    "    [SparCommand]\n    public Task TagAsync(string[] tags) => Task.CompletedTask;\n\n" +
    "    [SparCommand]\n    public Task<DateTime> NowAsync() => Task.FromResult(DateTime.UtcNow);\n\n" + Marker, StringComparison.Ordinal);
await Notify("textDocument/didChange", new JsonObject
{
    ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = 2 },
    ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = broken }),
});
Log("changed in memory");
await WaitFor(found => found.Any(f => f.StartsWith("SPAR015")) && found.Any(f => f.StartsWith("SPAR024")), "the unsaved edit");

await Notify("textDocument/didChange", new JsonObject
{
    ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = 3 },
    ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = original }),
});
Log("changed back");
await WaitFor(found => !found.Any(f => f.StartsWith("SPAR")), "the edit undone");

Console.WriteLine("file on disk unchanged: " + (File.ReadAllText(file) == original));
await Request("shutdown", null);
await Notify("exit", null);
process.WaitForExit(10000);
