#:property LangVersion=preview
#:property PublishAot=false

// Probe: does SDK 11.0.100's C# compiler accept the proposed union and closed-hierarchy syntaxes?
using System;

Console.WriteLine(Describe(new SaveError.InvalidText("Enter a note.")));
Console.WriteLine(Describe(new SaveError.Duplicate("n42")));

static string Describe(SaveError error) => error switch
{
    SaveError.InvalidText e => $"invalid: {e.Reason}",
    SaveError.Duplicate e => $"duplicate of {e.ExistingId}",
};

// Option 1: abstract record with nested sealed cases (works on any C# version).
public abstract record SaveError
{
    private SaveError() { }
    public sealed record InvalidText(string Reason) : SaveError;
    public sealed record Duplicate(string ExistingId) : SaveError;
}
