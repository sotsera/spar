namespace Spar.Tool.Doctor;

/// <summary>How much a finding matters: information, something to fix before it bites, or something that stops a build or a run.</summary>
internal enum Severity
{
    Info,
    Warning,
    Error,
}

/// <summary>What a finding is about: the machine, the app's project, or the build's identity.</summary>
internal enum Area
{
    Machine,
    Project,
    Identity,
}

/// <summary>
/// One result of <c>spar doctor</c>: a stable id (agents and scripts match on it; the message may change), its area,
/// its severity, what was found, and how to fix it when there is something to fix.
/// </summary>
internal sealed record Finding(string Id, Area Area, Severity Severity, string Message, string? Fix = null)
{
    public static Finding Info(string id, Area area, string message) => new(id, area, Severity.Info, message);

    public static Finding Warning(string id, Area area, string message, string fix) => new(id, area, Severity.Warning, message, fix);

    public static Finding Error(string id, Area area, string message, string fix) => new(id, area, Severity.Error, message, fix);
}

/// <summary>
/// The stable ids of <c>spar doctor</c>'s findings (decision "The spar tool (implementation record; choices for
/// review; 2026-11-22)"). An id is never reused for another meaning.
/// </summary>
internal static class FindingIds
{
    // The machine.
    public const string DotnetSdk = "dotnet-sdk";
    public const string SparSdk = "spar-sdk";
    public const string ToolVersion = "spar-tool-version";
    public const string Node = "node";
    public const string NodeVersionManager = "node-version-manager";
    public const string Npm = "npm";
    public const string WebView2 = "webview2-runtime";
    public const string WindowsNativeAot = "windows-nativeaot";
    public const string LongPaths = "windows-long-paths";
    public const string Gtk = "linux-gtk4";
    public const string WebKitGtk = "linux-webkitgtk";
    public const string Sandbox = "linux-sandbox-tools";
    public const string Icu = "linux-icu";
    public const string OpenSsl = "linux-openssl";
    public const string Clang = "linux-clang";
    public const string Zlib = "linux-zlib";

    // The project.
    public const string Project = "project";
    public const string FrontendFolder = "frontend-folder";
    public const string SparDependency = "frontend-spar-dependency";
    public const string VitePlugin = "frontend-vite-plugin";
    public const string Scripts = "frontend-scripts";
    public const string Lockfile = "frontend-lockfile";
    public const string NodeModules = "frontend-node-modules";
    public const string TypeCheck = "frontend-type-check";
    public const string ContractSnapshot = "contract-snapshot";
    public const string LaunchProfiles = "launch-profiles";

    // The build's identity.
    public const string Identity = "build-identity";
    public const string IdentityContracts = "build-identity-contracts";
    public const string IdentityDirty = "build-identity-dirty";
}
