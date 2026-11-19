namespace Spar;

/// <summary>Who draws the window's title bar.</summary>
public enum TitleBar
{
    /// <summary>The operating system's title bar.</summary>
    System,

    /// <summary>The page draws it: <c>data-spar-titlebar</c> marks the drag area, <c>data-spar-caption</c> the buttons.</summary>
    Custom,
}

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
