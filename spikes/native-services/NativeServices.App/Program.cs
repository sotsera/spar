using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NativeServices.App;

internal static class Program
{
    private static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "all";
        Console.WriteLine($"pid={Environment.ProcessId} dynamicCodeCompiled={RuntimeFeature.IsDynamicCodeCompiled} mode={mode} " +
            $"parts={Parts()}");

        switch (mode)
        {
#if PART_CREDMAN
            case "cred-write-peer":
                return Credentials.CredentialChecks.WriteForPeer();
            case "cred-read-peer":
                return Credentials.CredentialChecks.ReadAndDeleteForPeer();
            case "cred-verify":
                return Credentials.CredentialChecks.VerifyNoneRemain();
#endif
            case "all":
                return RunAll();
#if PART_DIALOGS || PART_CSWIN32_DIALOGS
            case "dialogs":
                return RunDialogsOnly();
#endif
            default:
                Console.WriteLine($"unknown mode {mode}");
                return 2;
        }
    }

    private static int RunAll()
    {
        var clock = Stopwatch.StartNew();
        var folder = Path.Combine(Path.GetTempPath(), $"spar-native-services-{Environment.ProcessId}");
        Directory.CreateDirectory(folder);
        try
        {
#if PART_COMBASELINE
            Section("com-baseline", () => Report.Check("com baseline: IShellItem display name", Dialogs.ShellCom.BaselineProbe(folder),
                folder));
#endif
#if PART_DIALOGS || PART_CSWIN32_DIALOGS
            Section("dialogs", () => Dialogs.DialogChecks.Run(folder));
#endif
#if PART_GRANTS
            Section("grants", () => Grants.GrantChecks.Run(folder));
#endif
#if PART_CREDMAN
            Section("credman", Credentials.CredentialChecks.RunCredentialManager);
#endif
#if PART_DPAPI
            Section("dpapi", () => Credentials.CredentialChecks.RunDpapi(folder));
#endif
#if PART_PROTECTEDDATA
            Section("protecteddata", Credentials.CredentialChecks.RunProtectedData);
#endif
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        Console.WriteLine($"passed={Report.Passed} failed={Report.Failed} elapsed={clock.Elapsed.TotalSeconds:F1}s " +
            $"tempRemoved={!Directory.Exists(folder)}");
        return Report.Failed == 0 ? 0 : 1;
    }

#if PART_DIALOGS || PART_CSWIN32_DIALOGS
    private static int RunDialogsOnly()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"spar-native-services-{Environment.ProcessId}");
        Directory.CreateDirectory(folder);
        try
        {
            Section("dialogs", () => Dialogs.DialogChecks.Run(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        Console.WriteLine($"passed={Report.Passed} failed={Report.Failed}");
        return Report.Failed == 0 ? 0 : 1;
    }

#endif
    private static void Section(string name, Action body)
    {
        try
        {
            body();
        }
        catch (Exception e)
        {
            Report.Check($"{name}: section completed", false, e.ToString());
        }
    }

    private static string Parts()
    {
        var parts = new List<string>();
#if PART_DIALOGS
        parts.Add("Dialogs");
#endif
#if PART_COMBASELINE
        parts.Add("ComBaseline");
#endif
#if PART_CSWIN32_DIALOGS
        parts.Add("CsWin32Dialogs");
#endif
#if PART_GRANTS
        parts.Add("Grants");
#endif
#if PART_CREDMAN
        parts.Add("CredMan");
#endif
#if PART_DPAPI
        parts.Add("Dpapi");
#endif
#if PART_PROTECTEDDATA
        parts.Add("ProtectedData");
#endif
        return parts.Count == 0 ? "none" : string.Join(",", parts);
    }
}
