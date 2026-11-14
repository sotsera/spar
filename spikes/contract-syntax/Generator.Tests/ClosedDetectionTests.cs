using Microsoft.CodeAnalysis;
using Spar.Generator;

namespace Generator.Tests;

/// <summary>How the generator sees C# 15 'closed': from syntax in source and project references, from metadata otherwise.</summary>
public sealed class ClosedDetectionTests
{
    private const string Types = """
        namespace Lib;
        public closed record ClosedRecord { public sealed record A : ClosedRecord; }
        public abstract record AbstractRecord { public sealed record A : AbstractRecord; }
        public closed class ClosedClass { }
        public sealed class Derived : ClosedClass { }
        public sealed record SealedRecord(int X);
        public static class Outer { public closed record Nested { public sealed record A : Nested; } }
        """;

    public static TheoryData<string> Sources => ["source", "metadata", "project"];

    [Theory]
    [MemberData(nameof(Sources))]
    public void Detects(string origin)
    {
        var compilation = origin == "source"
            ? Harness.Compile(Types, "Lib")
            : Harness.Compile("public static class Nothing { }", "App", Harness.Library(Types, "Lib", metadata: origin == "metadata"));

        bool IsClosed(string name) =>
            ClosedTypes.IsClosed(compilation.GetTypeByMetadataName(name) ?? throw new InvalidOperationException(name), compilation, TestContext.Current.CancellationToken);

        Assert.True(IsClosed("Lib.ClosedRecord"));
        Assert.True(IsClosed("Lib.ClosedClass"));
        Assert.True(IsClosed("Lib.Outer+Nested"));
        Assert.False(IsClosed("Lib.AbstractRecord"));
        Assert.False(IsClosed("Lib.SealedRecord"));

        // A closed record is abstract and a record in every view; only 'closed' itself needs special handling.
        var closed = compilation.GetTypeByMetadataName("Lib.ClosedRecord")!;
        Assert.True(closed.IsAbstract);
        Assert.True(closed.IsRecord);
        Assert.Equal(["A"], ClosedTypes.FindDerived(closed, TestContext.Current.CancellationToken).Select(t => t.Name));

        // GetAttributes() hides the compiler's IsClosedTypeAttribute, so it cannot be used to detect 'closed'.
        Assert.DoesNotContain(closed.GetAttributes(), a => a.AttributeClass?.Name == "IsClosedTypeAttribute");
        Assert.Equal(origin == "metadata", closed.DeclaringSyntaxReferences.IsEmpty);
    }
}
