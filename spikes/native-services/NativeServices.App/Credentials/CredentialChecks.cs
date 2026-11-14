#if PART_CREDMAN || PART_DPAPI || PART_PROTECTEDDATA
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;

namespace NativeServices.App.Credentials;

/// <summary>Credential storage checks. Only fake secrets; every credential created here is deleted.</summary>
internal static class CredentialChecks
{
    public const string Prefix = "SparSpike/native-services/";

    private static readonly byte[] s_entropy = "Spar.NativeServicesSpike.v1"u8.ToArray();

    public static string FakeConnectionString() =>
        "Server=tcp:spike-sql.invalid,1433;Database=SparSpikeFake;User ID=spike_user;" +
        $"Password=FAKE-not-a-secret-{Guid.NewGuid():N};Encrypt=True;TrustServerCertificate=False";

#if PART_CREDMAN
    public static unsafe void RunCredentialManager()
    {
        Report.Check("credman: CREDENTIALW is 80 bytes on x64", sizeof(NativeCredential) == 80,
            $"sizeof={sizeof(NativeCredential)}");
        try
        {
            var secret = Encoding.UTF8.GetBytes(FakeConnectionString());
            var local = Prefix + "conn-local";
            CredentialManager.Write(local, "spike_user", secret, CredentialPersist.LocalMachine);
            var readLocal = CredentialManager.Read(local);
            Report.Check("credman: write/read LOCAL_MACHINE round trip",
                readLocal is not null && readLocal.Secret.AsSpan().SequenceEqual(secret) &&
                readLocal.Persist == CredentialPersist.LocalMachine && readLocal.UserName == "spike_user",
                $"bytes={secret.Length} lastWritten={readLocal?.LastWrittenUtc:O}");

            var session = Prefix + "conn-session";
            CredentialManager.Write(session, null, secret, CredentialPersist.Session);
            Report.Check("credman: write/read SESSION round trip",
                CredentialManager.Read(session)?.Persist == CredentialPersist.Session, "persist=Session");

            var replacement = Encoding.UTF8.GetBytes(FakeConnectionString());
            CredentialManager.Write(local, "spike_user", replacement, CredentialPersist.LocalMachine);
            Report.Check("credman: CredWrite on an existing target replaces the blob",
                CredentialManager.Read(local)!.Secret.AsSpan().SequenceEqual(replacement), "replaced");

            var max = Prefix + "max-blob";
            CredentialManager.Write(max, null, new byte[CredentialManager.MaxBlobBytes], CredentialPersist.Session);
            Report.Check("credman: 2560-byte blob (CRED_MAX_CREDENTIAL_BLOB_SIZE) accepted",
                CredentialManager.Read(max)?.Secret.Length == CredentialManager.MaxBlobBytes, "2560");
            var tooLarge = 0;
            try
            {
                CredentialManager.Write(Prefix + "too-large", null, new byte[CredentialManager.MaxBlobBytes + 1],
                    CredentialPersist.Session);
            }
            catch (Win32Exception e)
            {
                tooLarge = e.NativeErrorCode;
            }

            Report.Check("credman: 2561-byte blob refused", tooLarge != 0, $"win32 error={tooLarge}");

            var listed = CredentialManager.Enumerate(Prefix + "*");
            Report.Check("credman: CredEnumerate on the prefix lists exactly ours", listed.Count == 3,
                string.Join(", ", listed));
            Report.Check("credman: missing target reads as null (ERROR_NOT_FOUND)",
                CredentialManager.Read(Prefix + "missing") is null, "null");

            Report.Time("credman CredWrite (SESSION)", 50,
                () => CredentialManager.Write(Prefix + "bench", null, secret, CredentialPersist.Session));
            Report.Time("credman CredWrite (LOCAL_MACHINE)", 50,
                () => CredentialManager.Write(Prefix + "bench", null, secret, CredentialPersist.LocalMachine));
            Report.Time("credman CredRead", 50, () => CredentialManager.Read(Prefix + "bench"));
            Report.Time("credman CredEnumerate(prefix*)", 50, () => CredentialManager.Enumerate(Prefix + "*"));
            Report.Time("credman CredWrite+CredDelete (LOCAL_MACHINE)", 50, () =>
            {
                CredentialManager.Write(Prefix + "bench-delete", null, secret, CredentialPersist.LocalMachine);
                CredentialManager.Delete(Prefix + "bench-delete");
            });
        }
        finally
        {
            var deleted = 0;
            foreach (var target in CredentialManager.Enumerate(Prefix + "*"))
            {
                deleted += CredentialManager.Delete(target) ? 1 : 0;
            }

            var remaining = CredentialManager.Enumerate(Prefix + "*");
            Report.Check("credman: cleanup leaves no credential under the prefix", remaining.Count == 0,
                $"deleted={deleted} remaining={remaining.Count}");
        }
    }

    /// <summary>Process A writes; another process reads (see measure.sh). Shows there is no per-app isolation.</summary>
    public static int WriteForPeer()
    {
        var target = Prefix + "peer";
        CredentialManager.Write(target, "spike_user", Encoding.UTF8.GetBytes(FakeConnectionString()),
            CredentialPersist.Session);
        Console.WriteLine($"wrote {target}");
        return 0;
    }

    public static int ReadAndDeleteForPeer()
    {
        var target = Prefix + "peer";
        var credential = CredentialManager.Read(target);
        var deleted = CredentialManager.Delete(target);
        var text = credential is null ? "" : Encoding.UTF8.GetString(credential.Secret);
        Report.Check("credman: a different process of the same user reads the secret",
            text.Contains("Password=FAKE-not-a-secret-", StringComparison.Ordinal), $"deleted={deleted}");
        var remaining = CredentialManager.Enumerate("SparSpike/*");
        Report.Check("credman: nothing left under SparSpike/*", remaining.Count == 0, string.Join(", ", remaining));
        return Report.Failed == 0 ? 0 : 1;
    }

    public static int VerifyNoneRemain()
    {
        var remaining = CredentialManager.Enumerate("SparSpike/*");
        Report.Check("credman: CredEnumerate(\"SparSpike/*\") is empty", remaining.Count == 0, string.Join(", ", remaining));
        return remaining.Count == 0 ? 0 : 1;
    }
#endif

#if PART_DPAPI
    public static void RunDpapi(string folder)
    {
        var plain = Encoding.UTF8.GetBytes(FakeConnectionString());
        var sealedData = Dpapi.Protect(plain, s_entropy, "Spar spike fake connection");
        var file = Path.Combine(folder, "connection.dpapi");
        File.WriteAllBytes(file, sealedData);
        var reopened = Dpapi.Unprotect(File.ReadAllBytes(file), s_entropy);
        Report.Check("dpapi: protect -> file -> unprotect round trip", reopened.AsSpan().SequenceEqual(plain),
            $"plain={plain.Length}B sealed={sealedData.Length}B overhead={sealedData.Length - plain.Length}B");
        File.Delete(file);
        var descriptionBytes = Encoding.Unicode.GetBytes("Spar spike fake connection");
        Report.Check("dpapi: the description is stored in the blob in clear text (do not put secrets in it)",
            sealedData.AsSpan().IndexOf(descriptionBytes) >= 0, $"{descriptionBytes.Length} bytes found");

        Report.Check("dpapi: wrong entropy refused", FailsWith(() => Dpapi.Unprotect(sealedData, "other"u8)) != 0,
            $"win32 error={FailsWith(() => Dpapi.Unprotect(sealedData, "other"u8))}");
        var tampered = (byte[])sealedData.Clone();
        tampered[^5] ^= 0x5A;
        Report.Check("dpapi: tampered blob refused", FailsWith(() => Dpapi.Unprotect(tampered, s_entropy)) != 0,
            $"win32 error={FailsWith(() => Dpapi.Unprotect(tampered, s_entropy))}");

        var large = new byte[1024 * 1024];
        Random.Shared.NextBytes(large);
        Report.Time("dpapi Protect 1 MiB", 5, () => Dpapi.Protect(large, s_entropy));
        Report.Time("dpapi Protect (connection string)", 200, () => Dpapi.Protect(plain, s_entropy));
        Report.Time("dpapi Unprotect (connection string)", 200, () => Dpapi.Unprotect(sealedData, s_entropy));
    }

    private static int FailsWith(Action action)
    {
        try
        {
            action();
            return 0;
        }
        catch (Win32Exception e)
        {
            return e.NativeErrorCode;
        }
    }
#endif

#if PART_PROTECTEDDATA
    public static void RunProtectedData()
    {
        var plain = Encoding.UTF8.GetBytes(FakeConnectionString());
        var sealedData = ProtectedData.Protect(plain, s_entropy, DataProtectionScope.CurrentUser);
        Report.Check("protecteddata: round trip",
            ProtectedData.Unprotect(sealedData, s_entropy, DataProtectionScope.CurrentUser).AsSpan().SequenceEqual(plain),
            $"sealed={sealedData.Length}B");
#if PART_DPAPI
        Report.Check("protecteddata <-> direct CryptUnprotectData interoperate",
            Dpapi.Unprotect(sealedData, s_entropy).AsSpan().SequenceEqual(plain) &&
            ProtectedData.Unprotect(Dpapi.Protect(plain, s_entropy), s_entropy, DataProtectionScope.CurrentUser)
                .AsSpan().SequenceEqual(plain), "both directions");
#endif
        Report.Time("protecteddata Protect (connection string)", 200,
            () => ProtectedData.Protect(plain, s_entropy, DataProtectionScope.CurrentUser));
        Report.Time("protecteddata Unprotect (connection string)", 200,
            () => ProtectedData.Unprotect(sealedData, s_entropy, DataProtectionScope.CurrentUser));
    }
#endif
}
#endif
