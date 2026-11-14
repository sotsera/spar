#:property PublishAot=false
// Proposed rule: classes keep their name; interfaces drop the one .NET "I" prefix (CA1715 convention);
// both drop a trailing "Service"; then acronym-aware camelCase (the same algorithm as JsonNamingPolicy.CamelCase).
using System.Text.Json;

(string Name, bool Interface)[] cases =
[
    ("NotesService", false), ("IOStatsService", false), ("SQLServerMonitorService", false), ("HTMLExportService", false),
    ("CopyJobs", false), ("Service", false),
    ("INotes", true), ("IIOStats", true), ("ISQLServerMonitor", true), ("IOrders", true), ("ICopyJobsService", true),
];

foreach (var (name, isInterface) in cases)
{
    var core = isInterface && name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]) ? name[1..] : name;
    if (core.EndsWith("Service", StringComparison.Ordinal) && core.Length > "Service".Length) core = core[..^"Service".Length];
    Console.WriteLine($"{(isInterface ? "interface" : "class"),-9} {name,-24} -> {JsonNamingPolicy.CamelCase.ConvertName(core)}");
}
