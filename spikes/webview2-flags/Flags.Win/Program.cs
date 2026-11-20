using System.Text.Json;
using System.Text.Json.Nodes;
using WebView2Flags;

// drive <plan.json>              run a plan (driver, on the caller's desktop; children on a desktop of their own)
// host <job.json> <result.json>  one child run (started by the driver with STARTUPINFO.lpDesktop)
// netlog <out.txt> <file>...     summarize net logs
return args switch
{
    ["drive", var plan] => Driver.Run(plan),
    ["host", var job, var result] => Host(job, result),
    ["netlog", var output, .. var files] => NetLogSummary(output, files),
    ["slim", var runs] => SlimRuns(runs),
    _ => Usage(),
};

static int Host(string jobFile, string resultFile)
{
    var job = JsonNode.Parse(File.ReadAllText(jobFile))!.AsObject();
    JsonObject result = [];
    var thread = new Thread(() =>
    {
        var pump = new UiPump();
        SynchronizationContext.SetSynchronizationContext(pump);
        var hwnd = UiPump.CreateHostWindow(1016, 719);
        var task = new HostRun(job, hwnd).RunAsync();
        pump.Run(task);
        result = task.IsCompletedSuccessfully ? task.Result : new JsonObject { ["error"] = task.Exception?.ToString() ?? "not completed" };
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    File.WriteAllText(resultFile, result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    return result["error"] is null ? 0 : 1;
}

static int NetLogSummary(string output, string[] files)
{
    // Private (LAN) addresses of this machine are not committed.
    var text = System.Text.RegularExpressions.Regex.Replace(Driver.Redact(NetLog.Summarize(files)), @"\b(?:192\.168|10\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01]))\.\d{1,3}\.\d{1,3}\b", "<lan address>");
    File.WriteAllText(output, text);
    Console.WriteLine(text);
    return 0;
}

static int SlimRuns(string path)
{
    var lines = File.ReadAllLines(path).Where(l => l.Length > 0).Select(l => Driver.Slim(JsonNode.Parse(l)!.AsObject()).ToJsonString());
    File.WriteAllLines(path, lines);
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("usage: drive <plan.json> | host <job.json> <result.json> | netlog <out.txt> <file>...");
    return 2;
}
