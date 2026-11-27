namespace Spar;

/// <summary>Who draws the window's title bar.</summary>
public enum TitleBar
{
    /// <summary>The operating system's title bar.</summary>
    System,

    /// <summary>The page draws it: <c>data-spar-titlebar</c> marks the drag area, <c>data-spar-caption</c> the buttons.</summary>
    Custom,
}

