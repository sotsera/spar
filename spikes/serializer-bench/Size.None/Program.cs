// Baseline: the same runtime settings with no serialization, so the DTO record is the only shared code.
var note = new Bench.Note("t", ["a"], null, 1.5, true, 2);
Console.WriteLine(note.Text.Length + note.Tags.Count);
