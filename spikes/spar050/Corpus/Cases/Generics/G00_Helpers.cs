// Generic helpers in source for the G cases (not a case). Their own bodies are not reported: T is not a Result here.
using System.Diagnostics.CodeAnalysis;

namespace Corpus.Generics;

public static class Helpers
{
    public static T Make<T>() => default!;

    public static T? Find<T>() => default;

    public static T Create<T>()
        where T : new() => new T();

    public static T Activate<T>() => Activator.CreateInstance<T>();

    public static T Outer<T>() => Make<T>();

    public static T Zero<T>()
        where T : struct => default;

    public static T Identity<T>(T value) => value;

    public static bool IsDefault<T>(T value) => EqualityComparer<T>.Default.Equals(value, default);

    public static bool TryFind<T>(IReadOnlyList<T> items, Func<T, bool> match, [MaybeNullWhen(false)] out T found)
    {
        foreach (var item in items)
        {
            if (match(item))
            {
                found = item;
                return true;
            }
        }

        found = default;
        return false;
    }

    public static T ViaLambda<T>()
    {
        Func<T> make = () => default!;
        return make();
    }
}

public sealed class Cache<T>
{
    public T GetOrDefault() => default!;
}

public sealed class Pool<T>
    where T : new()
{
    public T Rent() => new T();
}

public sealed class Box<T>
{
    public T Item { get; set; } = default!;
}
