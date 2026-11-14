using System.Text.Json;
using Bench;

// (B) only: parse and write a Note and a Note[] with the System.Text.Json context.
var note = JsonSerializer.Deserialize("""{"text":"t","tags":["a"],"parent":null,"score":1.5,"pinned":true,"version":2}"""u8, BenchJsonContext.Default.Note)!;
var notes = JsonSerializer.Deserialize("""[{"text":"t","tags":[],"parent":null,"score":1,"pinned":false,"version":null}]"""u8, BenchJsonContext.Default.NoteArray)!;
Console.WriteLine(JsonSerializer.SerializeToUtf8Bytes(note, BenchJsonContext.Default.Note).Length
    + JsonSerializer.SerializeToUtf8Bytes(notes, BenchJsonContext.Default.NoteArray).Length);
