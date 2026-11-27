namespace WireShapes;

/// <summary>A settings record with a list and a nested, nullable record, as an app's would be.</summary>
public sealed record WireSettings(string Theme, int BatchSize, IReadOnlyList<string> Recent, WireLayout? Window);

/// <summary>A second settings record, kept in its own file.</summary>
public sealed record WireLayout(int Width, int Height);
