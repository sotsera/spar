namespace Spar.Tests;

/// <summary>
/// The slot-order test of the Windows host's hand-declared WebView2 interfaces (plan section 3, "Bindings"; pitfalls,
/// "Hand-written COM"): every <c>[GeneratedComInterface]</c> of <c>Spar.Windows</c> is found in <c>WebView2.h</c> by
/// its IID, and each of its methods, in declaration order, is the header's method in that vtable slot, counted from 3
/// after IUnknown's. A placeholder <c>SlotNNN_Name</c> must sit in slot NNN and name the header's method there. The
/// interfaces are read from the assembly's metadata (<see cref="ComSlots"/>), so the test sees exactly what the build compiled.
/// </summary>
public sealed class WebView2SlotTests
{
    /// <summary>The interfaces and slots M1 needs beyond the base set (plan section 3, "Interface slots M1 needs").</summary>
    private static readonly (string Interface, int Slot, string Method)[] Required =
    [
        ("ICoreWebView2_22", 123, "AddWebResourceRequestedFilterWithRequestSourceKinds"),
        ("ICoreWebView2_22", 33, "PostWebMessageAsString"),
        ("ICoreWebView2Environment10", 12, "AddBrowserProcessExited"),
        ("ICoreWebView2Controller", 24, "Close"),
        ("ICoreWebView2WebResourceRequestedEventArgs2", 8, "GetRequestedSourceKind"),
        ("ICoreWebView2Environment10", 20, "CreateCoreWebView2ControllerOptions"),
        ("ICoreWebView2Environment10", 21, "CreateCoreWebView2ControllerWithOptions"),
        ("ICoreWebView2ControllerOptions3", 10, "PutDefaultBackgroundColor"),
        ("ICoreWebView2_22", 105, "GetProfile"),
        ("ICoreWebView2Profile", 9, "PutPreferredColorScheme"),
        ("ICoreWebView2Environment10", 18, "GetProcessInfos"),
        ("ICoreWebView2ProcessInfoCollection", 4, "GetValueAtIndex"),
        ("ICoreWebView2ProcessInfo", 4, "GetKind"),
        ("ICoreWebView2_22", 3, "GetSettings"),
        ("ICoreWebView2_22", 36, "CallDevToolsProtocolMethod"),
        ("ICoreWebView2_22", 75, "AddDownloadStarting"),
        ("ICoreWebView2_22", 100, "AddContextMenuRequested"),
        ("ICoreWebView2Controller", 19, "AddAcceleratorKeyPressed"),
        ("ICoreWebView2Environment10", 15, "GetUserDataFolder"),
        ("ICoreWebView2Settings8", 36, "PutIsReputationCheckingRequired"),
        ("ICoreWebView2NavigationStartingEventArgs3", 12, "GetNavigationKind"),
        ("ICoreWebView2EnvironmentOptions", 3, "GetAdditionalBrowserArguments"),
        ("ICoreWebView2AcceleratorKeyPressedEventArgs2", 10, "PutIsBrowserAcceleratorKeyEnabled"),
        ("ICoreWebView2ContextMenuItemCollection", 5, "RemoveValueAtIndex"),
        ("ICoreWebView2Controller", 12, "MoveFocus"),
        ("ICoreWebView2Controller", 13, "AddMoveFocusRequested"),
        ("ICoreWebView2Controller", 23, "NotifyParentWindowPositionChanged"),
        ("ICoreWebView2MoveFocusRequestedEventArgs", 5, "PutHandled"),
        ("ICoreWebView2Settings9", 38, "PutIsNonClientRegionSupportEnabled"),
    ];

    /// <summary>
    /// The interfaces from the Windows SDK's headers rather than WebView2's: the file dialogs' (Interop/Shell.cs) and UI
    /// Automation's provider (Interop/Automation.cs).
    /// </summary>
    private static readonly string[] SdkInterfaces = ["IFileDialog", "IFileOpenDialog", "IShellItem", "IShellItemArray", "IRawElementProviderSimple"];

    [Fact]
    public void Every_declared_method_sits_in_its_header_slot()
    {
        // The SDK's interfaces are checked where the Windows SDK is installed (the development PC, GitHub's Windows
        // runners); elsewhere only WebView2's, whose header comes with the package.
        var sdk = SdkHeaders();
        var headers = new[] { Path.Combine(AppContext.BaseDirectory, "WebView2.h") }.Concat(sdk).ToList();
        var problems = ComSlots.Problems(headers, "Spar.Windows", name => sdk.Count == 0 && SdkInterfaces.Contains(name), out var declared);

        Assert.True(declared > 0);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>The newest Windows SDK's <c>um/ShObjIdl_core.h</c> and <c>um/UIAutomationCore.h</c>, or none where there is no SDK.</summary>
    private static List<string> SdkHeaders()
    {
        var kits = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "Include");
        if (!OperatingSystem.IsWindows() || !Directory.Exists(kits))
        {
            return [];
        }

        var newest = Directory.GetDirectories(kits)
            .Where(version => File.Exists(Path.Combine(version, "um", "ShObjIdl_core.h")) && File.Exists(Path.Combine(version, "um", "UIAutomationCore.h")))
            .Order(StringComparer.Ordinal)
            .LastOrDefault();
        return newest is null ? [] : [Path.Combine(newest, "um", "ShObjIdl_core.h"), Path.Combine(newest, "um", "UIAutomationCore.h")];
    }

    [Fact]
    public void The_slots_M1_needs_are_declared()
    {
        var declared = ComSlots.Declared("Spar.Windows").ToDictionary(com => com.Name, StringComparer.Ordinal);

        foreach (var (name, slot, method) in Required)
        {
            Assert.True(declared.TryGetValue(name, out var com), $"{name} is not declared");
            Assert.True(com.Methods.Count > slot - 3 && com.Methods[slot - 3] == method, $"{name} slot {slot} is not {method}");
        }
    }
}
