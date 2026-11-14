// Library code the consumer sees only as metadata. Built without the analyzer, so nothing here is reported.
using System.Diagnostics.CodeAnalysis;
using Spar;

namespace Library;

/// <summary>Why a library lookup failed.</summary>
public closed record LibError
{
    public sealed record Missing(string Key) : LibError;
}

public static class Factory
{
    /// <summary>Returns default with no hint in the signature.</summary>
    public static Result<string, LibError> Make() => default;

    /// <summary>Declares that it may return default.</summary>
    [return: MaybeNull]
    public static Result<string, LibError> Find(string key) => key.Length > 0 ? Result(key) : default;

    private static Result<string, LibError> Result(string key) => key;

    public static bool TryMake(string key, [MaybeNullWhen(false)] out Result<string, LibError> result)
    {
        if (key.Length == 0)
        {
            result = default;
            return false;
        }

        result = key;
        return true;
    }

    /// <summary>An optional Result parameter whose default value is default(Result).</summary>
    public static string Describe(Result<string, LibError> result = default) => result.ToString();

    /// <summary>An array whose elements are default.</summary>
    public static Result<string, LibError>[] MakeArray(int count) => new Result<string, LibError>[count];
}

public static class Generic
{
    public static T Make<T>() => default!;

    public static T? Find<T>() => default;

    public static T Create<T>()
        where T : new() => new T();
}

/// <summary>A class whose Result field is never assigned.</summary>
public sealed class Holder
{
    public Result<string, LibError> Last;
}

/// <summary>A struct with a private Result field.</summary>
public readonly struct LibPair
{
    private readonly Result<string, LibError> _result;

    public LibPair(Result<string, LibError> result)
    {
        _result = result;
    }

    public Result<string, LibError> Result => _result;
}
