namespace Spar.Testing;

/// <summary>
/// Marks a scene (decision "M2c's decide-first questions", item 1): a <c>public static Task Name(SparScene scene)</c>
/// method in the app's end-to-end test project that starts the app through <see cref="SparScene.StartAsync{TApp}"/>
/// and brings its page to the state to capture. <c>spar snap name</c> captures it; <c>dotnet test</c> runs it as a
/// window test. The name is lower case letters, digits and hyphens, unique in the project.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class SparSceneAttribute(string name) : Attribute
{
    /// <summary>The scene's name, as <c>spar snap</c> takes it and names its folder.</summary>
    public string Name { get; } = name;
}
