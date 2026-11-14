using Spike.Subject;

namespace Spike.TUnitSuite;

public sealed class RomanTests
{
    public static IEnumerable<int> Numbers() => Enumerable.Range(1, 80);

    [Test]
    [MethodDataSource(nameof(Numbers))]
    public async Task Round_trips(int value) => await Assert.That(Roman.FromRoman(Roman.ToRoman(value))).IsEqualTo(value);
}

public sealed class SlugTests
{
    public static IEnumerable<(string Text, string Expected)> Cases() =>
    [
        ("Hello World", "hello-world"),
        ("  padded  ", "padded"),
        ("already-slugged", "already-slugged"),
        ("Multiple   spaces", "multiple-spaces"),
        ("Trailing!!!", "trailing"),
        ("!!!Leading", "leading"),
        ("MiXeD CaSe", "mixed-case"),
        ("numbers 123 here", "numbers-123-here"),
        ("under_score", "under-score"),
        ("dots.and.more", "dots-and-more"),
        ("a", "a"),
        ("", ""),
        ("---", ""),
        ("C# and .NET", "c-and-net"),
        ("tab\tseparated", "tab-separated"),
        ("new\nline", "new-line"),
        ("slash/path/like", "slash-path-like"),
        ("UPPER", "upper"),
        ("x  y  z", "x-y-z"),
        ("end-", "end"),
    ];

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task Creates(string text, string expected) => await Assert.That(Slug.Create(text)).IsEqualTo(expected);
}
