namespace Spar;

/// <summary>The contracts a window may call.</summary>
public sealed class CommandAllowList
{
    private readonly List<Type> _allowed;

    internal CommandAllowList(List<Type> allowed)
    {
        _allowed = allowed;
    }

    public CommandAllowList Allow<TContract>()
    {
        _allowed.Add(typeof(TContract));
        return this;
    }
}
