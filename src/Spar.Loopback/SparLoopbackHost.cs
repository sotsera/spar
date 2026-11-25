using System.ComponentModel;
using Spar.Runtime;

namespace Spar.Loopback;

/// <summary>
/// The loopback host as Spar's generated code creates it in a Development build (plan section 4, "Launch profiles"):
/// the app's backend with its page in an ordinary browser on 127.0.0.1, chosen by <see cref="Variable"/>, which the
/// <c>Loopback</c> launch profile sets (<c>dotnet run -lp Loopback</c>). The app's own arguments are never read for it.
/// Spar.Sdk references this assembly in Development builds only, so no other build has it or reads the variable.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SparLoopbackHost : ISparHost
{
    /// <summary>The variable that chooses the host: <c>Loopback</c>, or <c>Desktop</c> (the default) for the window.</summary>
    public const string Variable = "SPAR_HOST";

    private readonly string? _refused;

    /// <summary>Why the variable names no host; null for the loopback host itself.</summary>
    internal string? Refusal => _refused;

    private SparLoopbackHost(string? refused) => _refused = refused;

    /// <summary>
    /// The loopback host when <see cref="Variable"/> is <c>Loopback</c>; a host that stops the app with a message when
    /// it names neither host; null, for the desktop host, when it is unset, empty or <c>Desktop</c>. Case does not matter.
    /// </summary>
    public static ISparHost? Chosen() => Choose(Environment.GetEnvironmentVariable(Variable));

    public int Run(SparApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (_refused is not null)
        {
            Console.Error.WriteLine(_refused);
            return LoopbackApp.StartFailedExitCode;
        }

        return new LoopbackApp(app).Run();
    }

    /// <summary>What <see cref="Chosen"/> answers for the variable's value.</summary>
    internal static SparLoopbackHost? Choose(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name) || name.Equals("Desktop", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (name.Equals("Loopback", StringComparison.OrdinalIgnoreCase))
        {
            return new SparLoopbackHost(null);
        }

        return new SparLoopbackHost($"{Variable} is '{name}', which names no host: use Desktop (the window, the default) or Loopback (the page in a browser on 127.0.0.1, dotnet run -lp Loopback). Exit code {LoopbackApp.StartFailedExitCode}.");
    }
}
