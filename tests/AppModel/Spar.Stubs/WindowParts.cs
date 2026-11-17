namespace Spar;

/// <summary>Who draws the window's title bar.</summary>
public enum TitleBar
{
    /// <summary>The operating system's title bar.</summary>
    System,

    /// <summary>The page draws it: <c>data-spar-titlebar</c> marks the drag area, <c>data-spar-caption</c> the buttons.</summary>
    Custom,
}

public sealed class CspBuilder
{
    private readonly List<string> _fontSources = [];

    internal CspBuilder()
    {
    }

    internal IReadOnlyList<string> FontSources => _fontSources;

    /// <summary>Adds sources to <c>font-src</c> on top of the strict default.</summary>
    public CspBuilder FontSrc(params string[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _fontSources.AddRange(sources);
        return this;
    }
}

public sealed class PermissionsBuilder
{
    private readonly List<Permission> _allowed = [];

    internal PermissionsBuilder()
    {
    }

    internal IReadOnlyList<Permission> Allowed => _allowed;

    public PermissionsBuilder Allow(Permission permission)
    {
        _allowed.Add(permission);
        return this;
    }
}

/// <summary>Powerful browser features, denied unless allowed.</summary>
public enum Permission
{
    ClipboardRead,
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
