namespace Spar;

/// <summary>
/// The contracts, or single commands of a contract, a window may call (<see cref="WindowOptions.Commands"/>). The
/// names are checked against the app's contracts when the app starts, so a misspelled command fails there.
/// </summary>
public sealed class CommandAllowList
{
    private readonly List<Type> _contracts = [];
    private readonly List<(Type Contract, string Command)> _commands = [];

    internal CommandAllowList()
    {
    }

    internal IReadOnlyList<Type> Contracts => _contracts;

    internal IReadOnlyList<(Type Contract, string Command)> SingleCommands => _commands;

    /// <summary>Every command of the contract <typeparamref name="TContract"/> (its class or interface).</summary>
    public CommandAllowList Allow<TContract>()
    {
        _contracts.Add(typeof(TContract));
        return this;
    }

    /// <summary>
    /// The commands of <typeparamref name="TContract"/> named here by their names on the page (<c>get</c> for
    /// <c>GetAsync</c>, or a <c>[SparCommand("…")]</c> name).
    /// </summary>
    public CommandAllowList Allow<TContract>(params string[] commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Length == 0)
        {
            throw new ArgumentException("Name at least one command, or use Allow<TContract>() for all of them.", nameof(commands));
        }

        foreach (var command in commands)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(command);
            _commands.Add((typeof(TContract), command));
        }

        return this;
    }
}
