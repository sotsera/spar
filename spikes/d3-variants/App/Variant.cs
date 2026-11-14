namespace D3.App;

internal static class Variant
{
#if SPAR_VARIANT_DESKTOPTEST
    public const string Name = "DesktopTest";
#elif SPAR_VARIANT_DEVELOPMENT
    public const string Name = "Development";
#else
    public const string Name = "Production";
#endif
}
