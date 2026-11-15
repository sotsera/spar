using System.Text;
using Spar.Generator;
using Spar.Generator.Emit;

namespace Spar.Sdk.Tasks.Client;

/// <summary>
/// <c>&lt;Project&gt;.contracts.snapshot</c> (plan section 2, decided 2026-11-14): every command, record and error case
/// pages can see, one per line and in a stable order, so a change to what pages can call shows up as a reviewable diff.
/// Committed next to the project, the one generated file outside <c>obj/</c>.
/// </summary>
internal static class ContractSnapshot
{
    public static string Text(string projectName, IReadOnlyList<ClientContract> contracts)
    {
        var models = contracts.Select(c => c.Model).ToList();
        var text = new StringBuilder()
            .Append("# The contracts of ").Append(projectName).Append(" as pages see them. Written by the build; commit it with the change.\n")
            .Append("# CI builds fail with SPAR100 when it is out of date.\n")
            .Append('\n')
            .Append("fingerprint ").Append(CatalogEmitter.Fingerprint(models)).Append('\n');

        foreach (var contract in models.OrderBy(c => c.WireName, StringComparer.Ordinal))
        {
            text.Append('\n').Append("contract ").Append(contract.WireName).Append(" (").Append(Name(contract.CSharp)).Append(")\n");
            foreach (var command in contract.Commands.OrderBy(c => c.WireName, StringComparer.Ordinal))
            {
                var request = command.Request is null ? "" : Name(command.Request.CSharp);
                text.Append("  ").Append(command.WireName).Append('(').Append(request).Append(") -> ").Append(Returns(command)).Append('\n');
            }
        }

        foreach (var record in models.SelectMany(c => c.Records).DistinctBy(r => r.Key).OrderBy(r => Name(r.CSharp), StringComparer.Ordinal))
        {
            text.Append('\n').Append("record ").Append(Name(record.CSharp)).Append('\n');
            foreach (var property in record.Properties)
            {
                text.Append("  ").Append(property.JsonName).Append(": ").Append(Name(property.Type.CSharp)).Append('\n');
            }
        }

        foreach (var error in models.SelectMany(c => c.Errors).DistinctBy(e => e.Key).OrderBy(e => Name(e.CSharp), StringComparer.Ordinal))
        {
            text.Append('\n').Append("error ").Append(Name(error.CSharp)).Append('\n');
            foreach (var @case in error.Cases)
            {
                var fields = string.Join(", ", @case.Properties.Select(p => $"{p.JsonName}: {Name(p.Type.CSharp)}"));
                text.Append("  ").Append(@case.Code).Append(fields.Length == 0 ? "" : $"({fields})").Append('\n');
            }
        }

        return text.ToString();
    }

    private static string Returns(CommandModel command)
    {
        var value = command.Value is null ? "void" : Name(command.Value.CSharp);
        var error = command.ErrorCSharp is null ? "" : $" | {Name(command.ErrorCSharp)}";
        return value + error;
    }

    /// <summary>C# spellings without <c>global::</c> and without the namespaces of the BCL types wire values use.</summary>
    private static string Name(string csharp) => csharp
        .Replace("global::System.Collections.Generic.", "", StringComparison.Ordinal)
        .Replace("global::System.", "", StringComparison.Ordinal)
        .Replace("global::", "", StringComparison.Ordinal);
}
