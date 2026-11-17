namespace SqlTool.Support;

/// <summary>
/// Whether this is the support build, which the team sends to a user whose problem it cannot reproduce: the project
/// built with -p:DefineConstants=SUPPORT_BUILD. A shipped build keeps every browser default.
/// </summary>
public static class SupportBuild
{
#if SUPPORT_BUILD
    public static bool IsSupportBuild { get; } = true;
#else
    public static bool IsSupportBuild { get; }
#endif
}
