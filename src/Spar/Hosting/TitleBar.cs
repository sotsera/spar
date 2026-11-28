namespace Spar;

/// <summary>Who draws a window's title bar (<see cref="WindowOptions.TitleBar(TitleBar)"/>).</summary>
public enum TitleBar
{
    /// <summary>The operating system's title bar, the default.</summary>
    System,

    /// <summary>
    /// The page draws it (<c>docs/app-model.md</c> 6.9): <c>data-spar-titlebar</c> marks the drag area, and
    /// <c>data-spar-caption</c> the minimize, maximize and close buttons, which the SDK draws when the element is empty.
    /// </summary>
    Custom,
}
