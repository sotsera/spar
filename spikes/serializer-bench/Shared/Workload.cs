namespace Bench;

/// <summary>Deterministic benchmark data. The same objects feed both serializers.</summary>
public static class Workload
{
    private static readonly string[] Words =
    [
        "alpha", "bridge", "contract", "dispatch", "editor", "frontend", "generator", "host", "inline", "json",
        "kernel", "layout", "message", "native", "option", "payload", "query", "record", "serializer", "token",
    ];

    private static readonly string[] TagPool = ["work", "home", "urgent", "later", "idea", "draft", "review", "café"];

    public static Note Small() => Create(7, withParent: true);

    public static Note[] Array(int count)
    {
        var notes = new Note[count];
        for (var i = 0; i < count; i++)
        {
            notes[i] = Create(i, withParent: i % 3 == 0);
        }

        return notes;
    }

    private static Note Create(int i, bool withParent)
    {
        var random = new Random(i);
        var parent = withParent
            ? new Note(Text(random, i + 10_000), Tags(random, 2), null, random.NextDouble() * 100, false, null)
            : null;
        return new Note(
            Text(random, i),
            Tags(random, i % 5),
            parent,
            Math.Round(random.NextDouble() * 1000, i % 4 == 0 ? 15 : 2),
            i % 2 == 0,
            i % 4 == 1 ? null : 1_000_000L + (i * 37L));
    }

    private static string Text(Random random, int i)
    {
        var words = new string[4 + random.Next(6)];
        for (var w = 0; w < words.Length; w++)
        {
            words[w] = Words[random.Next(Words.Length)];
        }

        // Every tenth text needs escaping (a quote and a non-ASCII character).
        var suffix = i % 10 == 0 ? " \"quoted\" naïve" : string.Empty;
        return $"Note {i}: {string.Join(' ', words)}{suffix}";
    }

    private static string[] Tags(Random random, int count)
    {
        var tags = new string[count];
        for (var t = 0; t < count; t++)
        {
            tags[t] = TagPool[random.Next(TagPool.Length)];
        }

        return tags;
    }
}
