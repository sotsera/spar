// Wire-details probe: what System.Text.Json's Utf8JsonWriter/Utf8JsonReader actually do for the open wire questions.
// Usage (from spikes/wire-details): dotnet run --project WireProbe -c Release -- [section ...]
// Sections: long types surrogates surrogates-back null duplicates writer-errors (default: all but surrogates-back).
// Machine-readable payloads for the Node scripts go to results/*.json (relative to the current directory).

using WireProbe;

string[] all = ["long", "types", "surrogates", "null", "duplicates", "writer-errors"];
var sections = args.Length == 0 ? all : args;
Directory.CreateDirectory("results");

foreach (var section in sections)
{
    Console.WriteLine($"## {section}");
    switch (section)
    {
        case "long": LongProbe.Run(); break;
        case "types": TypesProbe.Run(); break;
        case "surrogates": SurrogateProbe.Run(); break;
        case "surrogates-back": SurrogateProbe.RunBack(); break;
        case "null": NullProbe.Run(); break;
        case "duplicates": DuplicateProbe.Run(); break;
        case "writer-errors": WriterErrorProbe.Run(); break;
        default: Console.WriteLine($"unknown section {section}"); break;
    }

    Console.WriteLine();
}
