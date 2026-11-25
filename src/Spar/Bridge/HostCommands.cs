using System.Text.Json;
using Microsoft.Extensions.Logging;
using Spar.Hosting;
using Spar.Runtime;

namespace Spar.Bridge;

/// <summary>
/// The commands a host answers itself, under the contract name <c>spar</c>, which no app contract can take (it is one
/// of the package's own subpaths): the SDK's page APIs that are not the app's code. One per window; its calls run on
/// the bridge's thread pool like the app's commands, never on the UI thread.
/// <list type="bullet">
/// <item><c>setColorScheme</c> (plan section 3d): the request is <c>"light"</c>, <c>"dark"</c> or <c>"system"</c>.
/// The choice is saved in the data root (<see cref="SavedColorScheme"/>) for the next start, and the window takes it at
/// once when its host can (<c>apply</c>): window colour, title bar, WebView background and <c>prefers-color-scheme</c>
/// together, as for a change of the system's theme. The declared error <c>notSaved</c> says the choice holds for this
/// run only.</item>
/// </list>
/// </summary>
internal sealed class HostCommands
{
    /// <summary>The contract name of the host's own commands.</summary>
    public const string Contract = "spar";

    /// <summary>The command that sets the app's colour scheme.</summary>
    public const string SetColorScheme = "setColorScheme";

    private static readonly byte[] NotSaved = """{"code":"notSaved"}"""u8.ToArray();

    private readonly string _dataRoot;
    private readonly Func<bool?, Task>? _apply;
    private readonly ILogger _logger;
    private readonly string _where;
    private readonly SemaphoreSlim _order = new(1, 1);

    /// <param name="dataRoot">The app's data root, where the choice is saved.</param>
    /// <param name="apply">
    /// Takes the choice in the window now (true dark, false light, null the system's), completing once it is applied;
    /// null for a host without a window of its own (loopback), where the browser keeps its own scheme.
    /// </param>
    /// <param name="logger">The host's log.</param>
    public HostCommands(string dataRoot, Func<bool?, Task>? apply, ILogger logger)
    {
        _dataRoot = dataRoot;
        _apply = apply;
        _logger = logger;
        _where = apply is null ? "saved for the window's next start; the browser keeps its own scheme" : "applied and saved";
    }

    /// <summary>Whether <paramref name="contract"/> names the host's own commands.</summary>
    public static bool IsHostContract(string contract) => string.Equals(contract, Contract, StringComparison.Ordinal);

    /// <summary>Runs one host command; one at a time, so two quick choices are saved and applied in the order sent.</summary>
    public async ValueTask<SparOutcome> InvokeAsync(string command, ReadOnlyMemory<byte> request, CancellationToken cancellationToken)
    {
        if (!string.Equals(command, SetColorScheme, StringComparison.Ordinal))
        {
            return SparOutcome.FromFailure(SparFailure.UnknownCommand(Contract, command));
        }

        if (ReadScheme(request.Span) is not { } name)
        {
            return SparOutcome.FromFailure(SparFailure.InvalidRequest("setColorScheme takes \"light\", \"dark\" or \"system\"."));
        }

        await _order.WaitAsync(cancellationToken);
        try
        {
            return await ChooseAsync(name, cancellationToken);
        }
        finally
        {
            _order.Release();
        }
    }

    /// <summary>The request's scheme name, or null when it is not one of the three.</summary>
    internal static string? ReadScheme(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = 1 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.String)
            {
                return null;
            }

            var name = reader.GetString();
            if (reader.Read())
            {
                return null;
            }

            return name is "light" or "dark" or "system" ? name : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<SparOutcome> ChooseAsync(string name, CancellationToken cancellationToken)
    {
        var dark = SavedColorScheme.Parse(name);
        var saved = true;
        try
        {
            SavedColorScheme.Write(_dataRoot, dark);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            saved = false;
            _logger.LogWarning(exception, "The colour scheme {Scheme} could not be saved; it holds for this run only.", name);
        }

        if (_apply is { } apply)
        {
            try
            {
                await apply(dark).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The document went away meanwhile; the window takes the choice anyway, as it was posted.
            }
        }

        if (!saved)
        {
            return SparOutcome.FromError(NotSaved);
        }

        _logger.LogInformation("Colour scheme {Scheme} chosen by the page: {Where}.", name, _where);
        return SparOutcome.FromValue(ReadOnlyMemory<byte>.Empty);
    }
}
