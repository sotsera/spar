#if SPAR_VARIANT_DESKTOPTEST
namespace D3.App;

// Compiled only into the DesktopTest variant. The marker is unique so a byte scan cannot match anything else.
internal static class DesktopTestHooks
{
    public static string Describe() => "hooks: D3MARKER-DESKTOPTEST-6b1f0c";
}
#endif
