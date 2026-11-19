using System.Text.Json;
using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>
/// Keys for any character (decision "Window tests: closing, session-end budget, test theme and any-character keys"):
/// a character the US keyboard has no key for still sends <c>keydown</c> and <c>keyup</c> with <c>event.key</c> the
/// character and an empty <c>event.code</c>, and text is split into Unicode scalar values, never UTF-16 units, so an
/// emoji is never two replacement characters. What the page receives is read from its own listeners.
/// </summary>
[Trait("Surface", "Window")]
public sealed class KeyTests
{
    private const string Mixed = "Grüße € ñ 😀 𝄞 中文 e\u0301!";

    [Theory]
    [InlineData("è")]
    [InlineData("€")]
    [InlineData("ñ")]
    [InlineData("😀")]
    [InlineData("中")]
    public async Task Pressing_a_character_off_the_US_keyboard_sends_its_key_events(string character)
    {
        await using var host = await HostPage.OpenAsync();
        var keys = host.Page.GetByLabel("Keys");

        await keys.PressAsync(character);

        var events = await EventsAsync(host);
        Report(character, events);
        Assert.Equal(["keydown", "keypress", "beforeinput", "input", "keyup"], events.Select(e => e.Type));
        Assert.All(events.Where(e => e.Type is "keydown" or "keypress" or "keyup"), e => Assert.Equal((character, ""), (e.Key, e.Code)));
        Assert.Equal(character, events.Single(e => e.Type == "input").Data);
        await Expect(keys).ToHaveValueAsync(character);
        host.AssertClean();
    }

    [Fact]
    public async Task A_character_on_the_US_keyboard_keeps_its_code()
    {
        await using var host = await HostPage.OpenAsync();

        await host.Page.GetByLabel("Keys").PressAsync("Shift+A");

        var down = (await EventsAsync(host)).First(e => e.Type == "keydown" && e.Key == "A");
        Assert.Equal("KeyA", down.Code);
    }

    [Fact]
    public async Task Typing_sends_one_key_per_scalar_value_and_the_page_ends_with_the_text()
    {
        await using var host = await HostPage.OpenAsync();
        var keys = host.Page.GetByLabel("Keys");

        await keys.PressSequentiallyAsync(Mixed);

        var events = await EventsAsync(host);
        var downs = events.Where(e => e.Type == "keydown").Select(e => e.Key).ToList();
        Assert.Equal(Mixed.EnumerateRunes().Select(rune => rune.ToString()), downs);
        Assert.DoesNotContain(events, e => e.Key?.Contains('\uFFFD') == true || e.Data?.Contains('\uFFFD') == true);
        await Expect(keys).ToHaveValueAsync(Mixed);
        host.AssertClean();
    }

    [Fact]
    public async Task Fill_inserts_emoji_supplementary_characters_and_combining_sequences_whole()
    {
        await using var host = await HostPage.OpenAsync();
        var keys = host.Page.GetByLabel("Keys");

        await keys.FillAsync(Mixed);

        await Expect(keys).ToHaveValueAsync(Mixed);
        Assert.Equal(Mixed, await host.Page.EvaluateAsync<string>("document.getElementById('keys').value"));
        Assert.DoesNotContain(await EventsAsync(host), e => e.Type == "keydown");
        host.AssertClean();
    }

    [Fact]
    public async Task Typing_into_editable_content_keeps_emoji_whole()
    {
        await using var host = await HostPage.OpenAsync();
        var message = host.Page.GetByRole(SparRole.Textbox, new() { Name = "Message" });

        await message.PressSequentiallyAsync("ok 😀𝄞");

        await Expect(message).ToHaveTextAsync("ok 😀𝄞");
        host.AssertClean();
    }

    private static async Task<List<KeyEvent>> EventsAsync(HostPage host)
    {
        var json = await host.Page.EvaluateAsync<string>("JSON.stringify(window.hostKeys)");
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray().Select(e => new KeyEvent(
            e.GetProperty("type").GetString()!,
            e.GetProperty("key").GetString(),
            e.GetProperty("code").GetString(),
            e.GetProperty("data").GetString(),
            e.GetProperty("value").GetString()))];
    }

    /// <summary>What the page received, for the evidence: <c>artifacts/test-results/Spar.WindowTests/keys/</c>, a file per character.</summary>
    private static void Report(string character, List<KeyEvent> events)
    {
        var lines = events.Select(e => $"{e.Type} key={e.Key} code='{e.Code}' data={e.Data} value={e.Value}");
        var name = string.Join("-", character.EnumerateRunes().Select(rune => $"U+{rune.Value:X4}"));
        File.WriteAllLines(Path.Combine(Results.Folder("keys"), name + ".txt"), [character, .. lines]);
    }

    private sealed record KeyEvent(string Type, string? Key, string? Code, string? Data, string? Value);
}
