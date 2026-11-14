#:property LangVersion=preview
#:property PublishAot=false

// Probe: C# union types (proposal syntax `union Name(Case1, Case2)`).
using System;

SaveError a = new InvalidText("Enter a note.");
SaveError b = new Duplicate("n42");
Console.WriteLine(Describe(a));
Console.WriteLine(Describe(b));

static string Describe(SaveError error) => error switch
{
    InvalidText e => $"invalid: {e.Reason}",
};

public sealed record InvalidText(string Reason);
public sealed record Duplicate(string ExistingId);
public union SaveError(InvalidText, Duplicate);
