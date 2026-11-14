using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

[assembly: SupportedOSPlatform("windows10.0.17763")]

namespace WinApis.App;

/// <summary>
/// Probes Windows Runtime APIs from an unpackaged NativeAOT exe. Each probe prints one RESULT line; the exit code is 0
/// only when every compiled probe behaved as expected.
/// </summary>
internal static class Program
{
    private static readonly Stopwatch s_clock = Stopwatch.StartNew();
    private static int s_failures;

    [STAThread]
    private static int Main(string[] args)
    {
        s_clock.Restart();
        Console.WriteLine($"exe: {Path.GetFileName(Path.GetDirectoryName(Environment.ProcessPath))} apartment={Thread.CurrentThread.GetApartmentState()}");
#if SPIKE_ALL || SPIKE_TOAST
        Probe("identity", Probes.Identity);
        Probe("toast", Probes.Toast);
#endif
#if SPIKE_ALL || SPIKE_VAULT
        Probe("vault", Probes.Vault);
#endif
#if SPIKE_ALL || SPIKE_PICKER
        if (!args.Contains("--no-picker"))
        {
            Probe("picker", PickerProbe.Run);
        }
#endif
#if SPIKE_BASELINE
        Console.WriteLine("RESULT baseline ok");
#endif
        Console.WriteLine($"done at {s_clock.ElapsedMilliseconds} ms, failures={s_failures}");
        return s_failures == 0 ? 0 : 1;
    }

    private static void Probe(string name, Func<string> probe)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            var outcome = probe();
            Console.WriteLine($"RESULT {name} ok {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms: {outcome}");
        }
        catch (Exception exception)
        {
            s_failures++;
            Console.WriteLine($"RESULT {name} FAILED {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms: {exception.GetType().Name} 0x{exception.HResult:X8} {exception.Message}");
        }
    }
}

#if !SPIKE_NO_WINRT && (SPIKE_ALL || SPIKE_TOAST || SPIKE_VAULT)
internal static class Probes
{
    public const string Resource = "SparSpike.CsWinRT";

    /// <summary>Package identity: expected to be absent for this unpackaged exe.</summary>
    public static string Identity()
    {
        try
        {
            var package = Windows.ApplicationModel.Package.Current;
            return "has identity: " + package.Id.FullName;
        }
        catch (Exception exception)
        {
            return $"no package identity ({exception.GetType().Name} 0x{exception.HResult:X8})";
        }
    }

    /// <summary>Creates notifiers without showing anything: the parameterless one needs identity; the AUMID one does not.</summary>
    public static string Toast()
    {
        string withoutId;
        try
        {
            withoutId = "CreateToastNotifier() ok, Setting=" + Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier().Setting;
        }
        catch (Exception exception)
        {
            withoutId = $"CreateToastNotifier() threw {exception.GetType().Name} 0x{exception.HResult:X8}";
        }

        string withId;
        try
        {
            var notifier = Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(Resource);
            withId = $"CreateToastNotifier(\"{Resource}\") ok, Setting={notifier.Setting}";
        }
        catch (Exception exception)
        {
            withId = $"CreateToastNotifier(aumid) threw {exception.GetType().Name} 0x{exception.HResult:X8}";
        }

        var template = Windows.UI.Notifications.ToastNotificationManager.GetTemplateContent(Windows.UI.Notifications.ToastTemplateType.ToastText01);
        return $"{withoutId}; {withId}; template xml {template.GetXml().Length} chars (not shown)";
    }

    /// <summary>Adds, reads and removes one credential, then proves the resource has no entries left.</summary>
    public static string Vault()
    {
        var vault = new Windows.Security.Credentials.PasswordVault();
        var before = Count(vault);
        if (before != 0)
        {
            throw new InvalidOperationException($"{before} entries already exist for {Resource}; refusing to touch them.");
        }

        var secret = "spike-" + Guid.NewGuid().ToString("N");
        var credential = new Windows.Security.Credentials.PasswordCredential(Resource, "spike-user", secret);
        var timer = Stopwatch.StartNew();
        vault.Add(credential);
        var add = timer.Elapsed.TotalMilliseconds;
        string readBack;
        try
        {
            var retrieved = vault.Retrieve(Resource, "spike-user");
            retrieved.RetrievePassword();
            readBack = retrieved.Password == secret ? "password round-tripped" : "PASSWORD MISMATCH";
        }
        finally
        {
            vault.Remove(credential);
        }

        var after = Count(vault);
        if (after != 0 || readBack != "password round-tripped")
        {
            throw new InvalidOperationException($"after removal {after} entries remain; {readBack}");
        }

        return $"add {add:F1} ms, {readBack}, removed, entries before={before} after={after}";
    }

    /// <summary>FindAllByResource throws ERROR_NOT_FOUND (0x80070490) when the resource has no entries.</summary>
    public static int Count(Windows.Security.Credentials.PasswordVault vault)
    {
        try
        {
            return vault.FindAllByResource(Resource).Count;
        }
        catch (Exception exception) when (exception.HResult == unchecked((int)0x80070490))
        {
            return 0;
        }
    }
}
#endif
