using Spar;

namespace MyApp.Greetings;

/// <summary>What the page shows for a name.</summary>
public sealed record Greeting(string Message);

/// <summary>Why a name could not be greeted.</summary>
public closed record GreetError
{
    /// <summary>The name is empty, or only spaces.</summary>
    public sealed record NameMissing : GreetError;
}

/// <summary>Greets people by name. The page calls it as <c>greeting</c>.</summary>
[SparContract]
public sealed class GreetingService
{
    /// <summary>A greeting for <paramref name="name"/>, without its surrounding spaces.</summary>
    [SparCommand]
    public Task<Result<Greeting, GreetError>> GreetAsync(string name)
    {
        var trimmed = name.Trim();
        Result<Greeting, GreetError> result = trimmed.Length == 0
            ? new GreetError.NameMissing()
            : new Greeting($"Hello, {trimmed}!");
        return Task.FromResult(result);
    }
}
