namespace Bench;

/// <summary>The D1 spike's DTO: string, string list, nested nullable record, double, bool and nullable long.</summary>
public sealed record Note(string Text, IReadOnlyList<string> Tags, Note? Parent, double Score, bool Pinned, long? Version);
