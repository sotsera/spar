using Microsoft.Extensions.DependencyInjection;

namespace Spar;

/// <summary>What a tray click or menu item does without app code.</summary>
public enum TrayAction
{
    /// <summary>Shows and restores the main window.</summary>
    ShowWindow,

    /// <summary>Quits through the normal shutdown.</summary>
    Quit,
}

/// <summary>The tray icon, declared in <c>SparApplication.Create</c>.</summary>
public sealed class TrayOptions
{
    internal TrayOptions()
    {
    }

    internal string? IconPath { get; private set; }

    internal string? TooltipText { get; private set; }

    internal TrayAction Activate { get; private set; } = TrayAction.ShowWindow;

    internal TrayMenu MenuItems { get; } = new();

    /// <summary>The icon, embedded in the app.</summary>
    public TrayOptions Icon(string path)
    {
        IconPath = path;
        return this;
    }

    public TrayOptions Tooltip(string text)
    {
        TooltipText = text;
        return this;
    }

    /// <summary>What a left click does. Some Linux hosts send no clicks, so the same action belongs in the menu too.</summary>
    public TrayOptions OnActivate(TrayAction action)
    {
        Activate = action;
        return this;
    }

    public TrayOptions Menu(Action<TrayMenu> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(MenuItems);
        return this;
    }
}

/// <summary>The tray menu, in order.</summary>
public sealed class TrayMenu
{
    private readonly List<string> _items = [];

    internal TrayMenu()
    {
    }

    internal IReadOnlyList<string> Items => _items;

    public TrayMenu Item(string label, TrayAction action)
    {
        _items.Add($"{label} ({action})");
        return this;
    }

    /// <summary>An item whose handler gets a service from the app's container. <paramref name="id"/> lets <see cref="ITray"/> change it later.</summary>
    public TrayMenu Item<TService>(string id, string label, Action<TService> handler)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        _items.Add($"{label} [{id}] -> {typeof(TService).Name}");
        return this;
    }

    public TrayMenu Separator()
    {
        _items.Add("-");
        return this;
    }
}

/// <summary>Changes the tray at run time; injectable. The page reaches it only through the app's own contracts.</summary>
public interface ITray
{
    /// <summary>False where the desktop has no tray (for example GNOME without an extension).</summary>
    bool IsAvailable { get; }

    void SetTooltip(string text);

    void SetChecked(string itemId, bool isChecked);

    void SetEnabled(string itemId, bool isEnabled);
}
