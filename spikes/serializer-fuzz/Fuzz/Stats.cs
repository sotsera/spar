using System.Globalization;

namespace Fuzz;

internal sealed class Group
{
    public long Count { get; set; }

    public long RepId { get; set; } = long.MaxValue;

    public int RepLength { get; set; } = int.MaxValue;

    public void Add(long count, long id, int length)
    {
        Count += count;
        if (length < RepLength || (length == RepLength && id < RepId))
        {
            RepId = id;
            RepLength = length;
        }
    }
}

/// <summary>
/// Counts per (comparison, stream, class) and groups per (comparison, class, key), mergeable across workers.
/// Wire format: tab-separated <c>C</c> and <c>G</c> lines.
/// </summary>
internal sealed class Stats
{
    public Dictionary<(string Cmp, string Stream, string Class), long> Counts { get; } = [];

    public Dictionary<(string Cmp, string Class, string Key), Group> Groups { get; } = [];

    public void Add(string cmp, CaseStream stream, string cls, string key, long id, int length)
    {
        var countKey = (cmp, stream.ToString(), cls);
        Counts[countKey] = Counts.GetValueOrDefault(countKey) + 1;
        var groupKey = (cmp, cls, key);
        if (!Groups.TryGetValue(groupKey, out var group))
        {
            Groups[groupKey] = group = new Group();
        }

        group.Add(1, id, length);
    }

    public void Write(TextWriter writer)
    {
        foreach (var ((cmp, stream, cls), count) in Counts)
        {
            writer.Write(FormattableString.Invariant($"C\t{cmp}\t{stream}\t{cls}\t{count}\n"));
        }

        foreach (var ((cmp, cls, key), group) in Groups)
        {
            writer.Write(FormattableString.Invariant($"G\t{cmp}\t{cls}\t{key}\t{group.Count}\t{group.RepId}\t{group.RepLength}\n"));
        }
    }

    public bool Merge(string line)
    {
        var parts = line.Split('\t');
        switch (parts[0])
        {
            case "C" when parts.Length == 5:
                var countKey = (parts[1], parts[2], parts[3]);
                Counts[countKey] = Counts.GetValueOrDefault(countKey) + long.Parse(parts[4], CultureInfo.InvariantCulture);
                return true;
            case "G" when parts.Length == 7:
                var groupKey = (parts[1], parts[2], parts[3]);
                if (!Groups.TryGetValue(groupKey, out var group))
                {
                    Groups[groupKey] = group = new Group();
                }

                group.Add(
                    long.Parse(parts[4], CultureInfo.InvariantCulture),
                    long.Parse(parts[5], CultureInfo.InvariantCulture),
                    int.Parse(parts[6], CultureInfo.InvariantCulture));
                return true;
            default:
                return false;
        }
    }
}
