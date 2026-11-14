#:property LangVersion=preview
#:property PublishAot=false

// Probe: closed hierarchies (proposal: `closed` modifier makes derived types known, so switches are exhaustive).
using System;

Console.WriteLine(Describe(new SaveError.InvalidText("Enter a note.")));
Console.WriteLine(Describe(new SaveError.Duplicate("n42")));

static string Describe(SaveError error) => error switch
{
    SaveError.InvalidText e => $"invalid: {e.Reason}",
    SaveError.Duplicate e => $"duplicate of {e.ExistingId}",
};

public closed record SaveError
{
    public sealed record InvalidText(string Reason) : SaveError;
    public sealed record Duplicate(string ExistingId) : SaveError;
}
