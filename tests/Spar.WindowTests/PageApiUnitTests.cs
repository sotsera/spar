using Spar.Testing;

namespace Spar.WindowTests;

/// <summary>
/// The page API's parts that need no window: key names, address globs, and how locators and failures read. These run
/// with the fast tests (no trait), the window tests in their own invocation.
/// </summary>
public sealed class PageApiUnitTests
{
    [Theory]
    [InlineData("Enter", "Enter", "Enter", 13, 0)]
    [InlineData("a", "a", "KeyA", 65, 0)]
    [InlineData("Control+End", "End", "End", 35, 2)]
    [InlineData("ControlOrMeta+a", "a", "KeyA", 65, 2)]
    [InlineData("Shift+Tab", "Tab", "Tab", 9, 8)]
    [InlineData("Control+Shift+Z", "Z", "KeyZ", 90, 10)]
    [InlineData("+", "+", "Equal", 187, 0)]
    [InlineData("Control++", "+", "Equal", 187, 2)]
    [InlineData("/", "/", "Slash", 191, 0)]
    public void Key_combinations_read_as_the_DevTools_Protocol_takes_them(string combination, string key, string code, int virtualKey, int modifiers)
    {
        var (parsedModifiers, parsed) = KeyboardLayout.Parse(combination);

        Assert.Equal((key, code, virtualKey, modifiers), (parsed.Name, parsed.Code, parsed.VirtualKey, (int)parsedModifiers));
    }

    [Fact]
    public void An_unknown_key_or_modifier_is_refused_with_its_name()
    {
        var key = Assert.Throws<ArgumentException>(() => KeyboardLayout.Parse("Control+Launch"));
        var modifier = Assert.Throws<ArgumentException>(() => KeyboardLayout.Parse("Hyper+a"));

        Assert.Contains("\"Launch\"", key.Message, StringComparison.Ordinal);
        Assert.Contains("\"Hyper\"", modifier.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Characters_off_the_US_keyboard_have_no_key()
    {
        Assert.Equal(("A", true), (KeyboardLayout.ForCharacter('A')!.Value.Key.Name, KeyboardLayout.ForCharacter('A')!.Value.Shift));
        Assert.True(KeyboardLayout.ForCharacter('?')!.Value.Shift);
        Assert.Null(KeyboardLayout.ForCharacter('ü'));
        Assert.Null(KeyboardLayout.ForCharacter('—'));
    }

    [Theory]
    [InlineData("**/settings", "https://app.localhost/settings", true)]
    [InlineData("**/settings", "https://app.localhost/settings/more", false)]
    [InlineData("https://app.localhost/*", "https://app.localhost/other.html", true)]
    [InlineData("https://app.localhost/*", "https://app.localhost/a/b", false)]
    [InlineData("https://app.localhost/page?.html", "https://app.localhost/page2.html", true)]
    [InlineData("https://app.localhost/", "https://app.localhost/", true)]
    public void Address_globs_match_as_the_browser_automation_librarys_do(string glob, string url, bool matches)
    {
        Assert.Equal(matches, SparPage.Glob(glob).IsMatch(url));
    }

    [Fact]
    public void A_locator_reads_as_the_test_wrote_it()
    {
        var steps = new[]
        {
            LocatorStep.Role(SparRole.Dialog, new() { Name = "New page" }),
            LocatorStep.Text("label", "GetByLabel", "Title", null, new() { Exact = true }),
            LocatorStep.Nth(-1),
            LocatorStep.Filter(new() { HasText = "draft" }),
        };

        var described = string.Join(".", steps.Select(step => step.Description));

        Assert.Equal("GetByRole(Dialog, \"New page\").GetByLabel(\"Title\", Exact).Last.Filter(HasText = \"draft\")", described);
    }
}
