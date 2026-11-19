using System.Collections.Frozen;
using System.Text;

namespace Spar.Testing;

/// <summary>
/// The US keyboard as the DevTools Protocol's <c>Input.dispatchKeyEvent</c> takes it: each key's <c>key</c>,
/// <c>code</c>, Windows virtual-key code and text, and the shifted characters. Key names follow the UI Events
/// <c>key</c> values, as the browser automation library's do ("Enter", "ArrowDown", "Control+End"). A character the US
/// keyboard has no key for (<c>è</c>, <c>€</c>, an emoji) is still a key: its <c>key</c> is the character, its
/// <c>code</c> empty and its virtual-key code 0, so the page sees <c>keydown</c> and <c>keyup</c> with the right
/// <c>event.key</c> and the text arrives as typed. Dead keys, other layouts and input-method composition are not modelled.
/// </summary>
internal static class KeyboardLayout
{
    /// <summary>Named keys: code, virtual-key code, and the text they type (null for none).</summary>
    private static readonly FrozenDictionary<string, Key> Named = new Dictionary<string, Key>(StringComparer.Ordinal)
    {
        ["Enter"] = new("Enter", "Enter", 13, "\r"),
        ["Tab"] = new("Tab", "Tab", 9, null),
        ["Escape"] = new("Escape", "Escape", 27, null),
        ["Backspace"] = new("Backspace", "Backspace", 8, null),
        ["Delete"] = new("Delete", "Delete", 46, null),
        ["Insert"] = new("Insert", "Insert", 45, null),
        ["Home"] = new("Home", "Home", 36, null),
        ["End"] = new("End", "End", 35, null),
        ["PageUp"] = new("PageUp", "PageUp", 33, null),
        ["PageDown"] = new("PageDown", "PageDown", 34, null),
        ["ArrowLeft"] = new("ArrowLeft", "ArrowLeft", 37, null),
        ["ArrowUp"] = new("ArrowUp", "ArrowUp", 38, null),
        ["ArrowRight"] = new("ArrowRight", "ArrowRight", 39, null),
        ["ArrowDown"] = new("ArrowDown", "ArrowDown", 40, null),
        ["Shift"] = new("Shift", "ShiftLeft", 16, null),
        ["Control"] = new("Control", "ControlLeft", 17, null),
        ["Alt"] = new("Alt", "AltLeft", 18, null),
        ["Meta"] = new("Meta", "MetaLeft", 91, null),
        ["Space"] = new(" ", "Space", 32, " "),
        ["F1"] = new("F1", "F1", 112, null),
        ["F2"] = new("F2", "F2", 113, null),
        ["F3"] = new("F3", "F3", 114, null),
        ["F4"] = new("F4", "F4", 115, null),
        ["F5"] = new("F5", "F5", 116, null),
        ["F6"] = new("F6", "F6", 117, null),
        ["F7"] = new("F7", "F7", 118, null),
        ["F8"] = new("F8", "F8", 119, null),
        ["F9"] = new("F9", "F9", 120, null),
        ["F10"] = new("F10", "F10", 121, null),
        ["F11"] = new("F11", "F11", 122, null),
        ["F12"] = new("F12", "F12", 123, null),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Punctuation: the unshifted and shifted character of each key, its code and virtual-key code.</summary>
    private static readonly (char Plain, char Shifted, string Code, int VirtualKey)[] Punctuation =
    [
        ('`', '~', "Backquote", 192), ('-', '_', "Minus", 189), ('=', '+', "Equal", 187), ('[', '{', "BracketLeft", 219),
        (']', '}', "BracketRight", 221), ('\\', '|', "Backslash", 220), (';', ':', "Semicolon", 186), ('\'', '"', "Quote", 222),
        (',', '<', "Comma", 188), ('.', '>', "Period", 190), ('/', '?', "Slash", 191), ('1', '!', "Digit1", 49),
        ('2', '@', "Digit2", 50), ('3', '#', "Digit3", 51), ('4', '$', "Digit4", 52), ('5', '%', "Digit5", 53),
        ('6', '^', "Digit6", 54), ('7', '&', "Digit7", 55), ('8', '*', "Digit8", 56), ('9', '(', "Digit9", 57),
        ('0', ')', "Digit0", 48),
    ];

    /// <summary>
    /// A key combination such as <c>"Control+End"</c>, <c>"Shift+Tab"</c> or <c>"a"</c>: the modifiers, then the key.
    /// <c>ControlOrMeta</c> is Control on Windows and Linux.
    /// </summary>
    public static (Modifiers Modifiers, Key Key) Parse(string combination)
    {
        ArgumentException.ThrowIfNullOrEmpty(combination);
        var parts = combination.Length > 1 && combination.EndsWith("++", StringComparison.Ordinal)
            ? [.. combination[..^2].Split('+', StringSplitOptions.RemoveEmptyEntries), "+"]
            : combination == "+" ? ["+"] : combination.Split('+');
        var modifiers = Modifiers.None;
        foreach (var part in parts[..^1])
        {
            modifiers |= part switch
            {
                "Shift" => Modifiers.Shift,
                "Control" or "ControlOrMeta" => Modifiers.Control,
                "Alt" => Modifiers.Alt,
                "Meta" => Modifiers.Meta,
                _ => throw new ArgumentException($"Unknown modifier \"{part}\" in \"{combination}\".", nameof(combination)),
            };
        }

        var key = Find(parts[^1]) ?? throw new ArgumentException(
            $"Unknown key \"{parts[^1]}\" in \"{combination}\": use one character, or a key name such as Enter, Tab, Escape, ArrowDown or F5.",
            nameof(combination));
        return (modifiers, key);
    }

    /// <summary>The key that types <paramref name="rune"/> on a US keyboard, with whether Shift is held; null when none does.</summary>
    public static (Key Key, bool Shift)? ForCharacter(Rune rune)
    {
        if (!rune.IsBmp)
        {
            return null;
        }

        var character = (char)rune.Value;
        if (character is >= 'a' and <= 'z')
        {
            return (Letter(character, character), false);
        }

        if (character is >= 'A' and <= 'Z')
        {
            return (Letter(char.ToLowerInvariant(character), character), true);
        }

        foreach (var (plain, shifted, code, virtualKey) in Punctuation)
        {
            if (character == plain || character == shifted)
            {
                var text = character.ToString();
                return (new Key(text, code, virtualKey, text), character == shifted);
            }
        }

        return character switch
        {
            ' ' => (Named["Space"], false),
            '\n' or '\r' => (Named["Enter"], false),
            '\t' => (Named["Tab"], false),
            _ => null,
        };
    }

    private static Key? Find(string name)
    {
        if (Named.TryGetValue(name, out var named))
        {
            return named;
        }

        if (name == " ")
        {
            return Named["Space"];
        }

        if (!Rune.TryGetRuneAt(name, 0, out var rune) || rune.Utf16SequenceLength != name.Length)
        {
            return null;
        }

        return ForCharacter(rune) is { } typed ? typed.Key : Unmapped(rune);
    }

    /// <summary>
    /// A character with no key on the US keyboard, as one key: <c>key</c> and text the character, no <c>code</c>, no
    /// virtual-key code. One Unicode scalar value, so its text is at most two UTF-16 units, under DevTools' limit of three.
    /// </summary>
    public static Key Unmapped(Rune rune)
    {
        var text = rune.ToString();
        return new Key(text, "", 0, text);
    }

    private static Key Letter(char lower, char typed) =>
        new(typed.ToString(), "Key" + char.ToUpperInvariant(lower), char.ToUpperInvariant(lower), typed.ToString());

    /// <summary>One key: its UI Events <c>key</c> value, its <c>code</c>, its Windows virtual-key code and its text.</summary>
    public sealed record Key(string Name, string Code, int VirtualKey, string? Text);

    /// <summary>The DevTools Protocol's modifier bits.</summary>
    [Flags]
    public enum Modifiers
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Meta = 4,
        Shift = 8,
    }
}
