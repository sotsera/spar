using System.Text;

namespace Spike.Subject;

/// <summary>Small pure functions for the fast unit tests. Shared by every suite.</summary>
public static class Calc
{
    public static int Add(int left, int right) => left + right;

    public static int Multiply(int left, int right) => left * right;

    public static string Reverse(string value)
    {
        var chars = value.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    public static bool IsPalindrome(string value) => value == Reverse(value);
}

public static class Primes
{
    public static bool IsPrime(int value)
    {
        if (value < 2)
        {
            return false;
        }

        for (var divisor = 2; divisor * divisor <= value; divisor++)
        {
            if (value % divisor == 0)
            {
                return false;
            }
        }

        return true;
    }
}

public static class Roman
{
    private static readonly (int Value, string Symbol)[] s_symbols =
    [
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
        (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
    ];

    public static string ToRoman(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        var builder = new StringBuilder();
        foreach (var (symbolValue, symbol) in s_symbols)
        {
            while (value >= symbolValue)
            {
                builder.Append(symbol);
                value -= symbolValue;
            }
        }

        return builder.ToString();
    }

    public static int FromRoman(string text)
    {
        var total = 0;
        var index = 0;
        foreach (var (symbolValue, symbol) in s_symbols)
        {
            while (text.AsSpan(index).StartsWith(symbol, StringComparison.Ordinal))
            {
                total += symbolValue;
                index += symbol.Length;
            }
        }

        return index == text.Length ? total : throw new FormatException($"'{text}' is not a Roman numeral.");
    }
}

public static class Slug
{
    public static string Create(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingDash = false;
        foreach (var c in text.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(c));
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        return builder.ToString();
    }
}
