using Spike.Subject;

namespace Spike.XunitSuite;

public sealed class RomanTests
{
    public static TheoryData<int> Numbers => [.. Enumerable.Range(1, 80)];

    [Theory]
    [MemberData(nameof(Numbers))]
    public void Round_trips(int value) => Assert.Equal(value, Roman.FromRoman(Roman.ToRoman(value)));
}

public sealed class SlugTests
{
    public static TheoryData<string, string> Cases => new()
    {
        { "Hello World", "hello-world" },
        { "  padded  ", "padded" },
        { "already-slugged", "already-slugged" },
        { "Multiple   spaces", "multiple-spaces" },
        { "Trailing!!!", "trailing" },
        { "!!!Leading", "leading" },
        { "MiXeD CaSe", "mixed-case" },
        { "numbers 123 here", "numbers-123-here" },
        { "under_score", "under-score" },
        { "dots.and.more", "dots-and-more" },
        { "a", "a" },
        { "", "" },
        { "---", "" },
        { "C# and .NET", "c-and-net" },
        { "tab\tseparated", "tab-separated" },
        { "new\nline", "new-line" },
        { "slash/path/like", "slash-path-like" },
        { "UPPER", "upper" },
        { "x  y  z", "x-y-z" },
        { "end-", "end" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Creates(string text, string expected) => Assert.Equal(expected, Slug.Create(text));
}
