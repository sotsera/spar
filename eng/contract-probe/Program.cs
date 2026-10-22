using System.Diagnostics;

var log = Environment.GetEnvironmentVariable("SPAR_TEST_CONTRACT_CALLS")
    ?? throw new InvalidOperationException("The contract probe needs its caller-owned log.");
if (args.Length != 4)
{
    throw new ArgumentException("The probe forwards exactly four generator arguments.");
}

File.AppendAllText(log, DateTimeOffset.UtcNow.ToString("O") + Environment.NewLine);
var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Spar.ContractGenerator.dll"));
foreach (var argument in args)
{
    start.ArgumentList.Add(argument);
}

using var process = Process.Start(start) ?? throw new InvalidOperationException("Generator did not start.");
await process.WaitForExitAsync();
return process.ExitCode;
