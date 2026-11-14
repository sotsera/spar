using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Data.SqlClient;

namespace SqlTool;

// Linux-only checks. Each prints PASS/FAIL lines and the native libraries the process has mapped.
//   env            runtime, ICU, code pages, mapped libraries (no server)
//   transport      data-source prefixes: tcp, default, np, lpc, admin, local names
//   tls            Encrypt / TrustServerCertificate / HostNameInCertificate combinations
//   auth           Integrated Security (Kerberos through GSSAPI) and other non-SQL authentication modes
//   globalization  varchar round trips through server code pages 1252 and 932, collation-sensitive comparisons
//   secret         libsecret store / lookup / clear through [LibraryImport] (needs D-Bus and a Secret Service)
internal static class LinuxProbe
{
    public static async Task<int> RunAsync(string what)
    {
        var failures = what switch
        {
            "env" => Env(),
            "transport" => await TransportAsync(),
            "tls" => await TlsAsync(),
            "auth" => await AuthAsync(),
            "globalization" => await GlobalizationAsync(),
            "secret" => SecretProbe.Run(),
            _ => throw new ArgumentException($"unknown probe '{what}'"),
        };
        Console.WriteLine($"LIBS {MappedLibraries()}");
        return failures;
    }

    private static int Env()
    {
        Console.WriteLine($"ENV os={RuntimeInformation.OSDescription} rid={RuntimeInformation.RuntimeIdentifier} " +
            $"framework={RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"ENV LANG={Environment.GetEnvironmentVariable("LANG") ?? "(unset)"} " +
            $"currentCulture={CultureInfo.CurrentCulture.Name} (harness sets invariant) " +
            $"installedUICulture={CultureInfo.InstalledUICulture.Name}");

        // ICU is in use when culture data is real: Turkish casing and Japanese culture names need it.
        var turkish = CultureInfo.GetCultureInfo("tr-TR").TextInfo.ToUpper("i");
        var japanese = CultureInfo.GetCultureInfo("ja-JP").NativeName;
        Console.WriteLine($"ENV icu turkishUpper(i)={(turkish == "İ" ? "U+0130 (ICU)" : turkish)} jaNative={japanese}");

        // SqlClient decodes varchar with the collation's code page: is 1252 / 932 available?
        foreach (var codePage in new[] { 1252, 932, 936, 949, 950, 1250, 1251 })
        {
            try
            {
                var encoding = Encoding.GetEncoding(codePage);
                Console.WriteLine($"ENV codepage {codePage} available ({encoding.WebName})");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"ENV codepage {codePage} unavailable: {Db.Describe(exception)}");
            }
        }

        return 0;
    }

    private static async Task<int> TransportAsync()
    {
        var host = HostOf(Db.A);
        string[] sources =
        [
            $"tcp:{host},1433", $"{host},1433", host, $"np:{host}", $@"np:\\{host}\pipe\sql\query", $"lpc:{host}",
            $"admin:{host}", ".", "(local)", "localhost", "np:.", "lpc:.",
        ];
        var failures = 0;
        foreach (var source in sources)
        {
            var builder = new SqlConnectionStringBuilder(Db.A)
            {
                DataSource = source,
                ConnectTimeout = 5,
                Pooling = false,
                ApplicationName = "spar-transport-check",
            };
            var watch = Stopwatch.StartNew();
            try
            {
                await using var connection = await Db.OpenAsync(builder.ConnectionString);
                var openMs = watch.Elapsed.TotalMilliseconds;
                Console.WriteLine($"PASS transport source={source} openMs={openMs:F1} {await ConnectionInfoAsync(connection)}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"FAIL transport source={source} after {watch.Elapsed.TotalMilliseconds:F0} ms: " +
                    Describe(exception));
            }
        }

        Console.WriteLine($"RESULT transport passes={sources.Length - failures}/{sources.Length}");
        return 0;
    }

    private static async Task<int> TlsAsync()
    {
        var host = HostOf(Db.A);
        (string Name, Action<SqlConnectionStringBuilder> Apply)[] cases =
        [
            ("optional", b => { b.Encrypt = SqlConnectionEncryptOption.Optional; b.TrustServerCertificate = false; }),
            ("optional+trust", b => { b.Encrypt = SqlConnectionEncryptOption.Optional; b.TrustServerCertificate = true; }),
            ("mandatory", b => { b.Encrypt = SqlConnectionEncryptOption.Mandatory; b.TrustServerCertificate = false; }),
            ("mandatory+trust", b => { b.Encrypt = SqlConnectionEncryptOption.Mandatory; b.TrustServerCertificate = true; }),
            ("mandatory+trust+hostname-mismatch", b =>
            {
                b.Encrypt = SqlConnectionEncryptOption.Mandatory;
                b.TrustServerCertificate = true;
                b.HostNameInCertificate = "not-the-server.example";
            }),
            ("strict", b => { b.Encrypt = SqlConnectionEncryptOption.Strict; b.TrustServerCertificate = false; }),
            ("strict+trust", b => { b.Encrypt = SqlConnectionEncryptOption.Strict; b.TrustServerCertificate = true; }),
        ];
        foreach (var (name, apply) in cases)
        {
            var builder = new SqlConnectionStringBuilder(Db.A)
            {
                DataSource = $"tcp:{host},1433",
                ConnectTimeout = 5,
                Pooling = false,
                ApplicationName = "spar-tls-check",
            };
            apply(builder);
            var watch = Stopwatch.StartNew();
            try
            {
                await using var connection = await Db.OpenAsync(builder.ConnectionString);
                var openMs = watch.Elapsed.TotalMilliseconds;
                Console.WriteLine($"PASS tls case={name} openMs={openMs:F1} {await ConnectionInfoAsync(connection)}");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"FAIL tls case={name} after {watch.Elapsed.TotalMilliseconds:F0} ms: " +
                    Describe(exception));
            }
        }

        return 0;
    }

    private static async Task<int> AuthAsync()
    {
        var host = HostOf(Db.A);
        (string Name, Func<SqlConnectionStringBuilder> Build)[] cases =
        [
            ("integrated-tcp", () => new SqlConnectionStringBuilder
            {
                DataSource = $"tcp:{host},1433", IntegratedSecurity = true,
            }),
            ("integrated-tcp-spn", () => new SqlConnectionStringBuilder
            {
                DataSource = $"tcp:{host},1433", IntegratedSecurity = true, ServerSPN = $"MSSQLSvc/{host}:1433",
            }),
            ("integrated-np", () => new SqlConnectionStringBuilder { DataSource = $"np:{host}", IntegratedSecurity = true }),
            ("ad-integrated", () => new SqlConnectionStringBuilder
            {
                DataSource = $"tcp:{host},1433", Authentication = SqlAuthenticationMethod.ActiveDirectoryIntegrated,
            }),
            ("ad-default", () => new SqlConnectionStringBuilder
            {
                DataSource = $"tcp:{host},1433", Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault,
            }),
        ];
        Console.WriteLine($"AUTH KRB5CCNAME={Environment.GetEnvironmentVariable("KRB5CCNAME") ?? "(unset)"} " +
            $"KRB5_CONFIG={Environment.GetEnvironmentVariable("KRB5_CONFIG") ?? "(unset)"} " +
            $"/etc/krb5.conf={(File.Exists("/etc/krb5.conf") ? "present" : "absent")}");
        foreach (var (name, build) in cases)
        {
            var builder = build();
            builder.Encrypt = SqlConnectionEncryptOption.Mandatory;
            builder.TrustServerCertificate = true;
            builder.ConnectTimeout = 5;
            builder.Pooling = false;
            var watch = Stopwatch.StartNew();
            try
            {
                await using var connection = await Db.OpenAsync(builder.ConnectionString);
                Console.WriteLine($"PASS auth case={name} {await ConnectionInfoAsync(connection)}");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"FAIL auth case={name} after {watch.Elapsed.TotalMilliseconds:F0} ms: " +
                    Describe(exception));
            }
        }

        return 0;
    }

    private static async Task<int> GlobalizationAsync()
    {
        var failures = 0;
        await using var connection = await Db.OpenAsync(Db.With(Db.A, "master"));
        await Db.ExecAsync(connection,
            """
            IF OBJECT_ID('tempdb..#g') IS NOT NULL DROP TABLE #g;
            CREATE TABLE #g (Id int NOT NULL, Latin varchar(40) COLLATE Latin1_General_CI_AS NULL,
                Japanese varchar(40) COLLATE Japanese_CI_AS NULL, Turkish varchar(40) COLLATE Turkish_CI_AS NULL,
                Unicode nvarchar(40) NULL);
            """);
        var latin = "café Größe €";
        var japanese = "日本語テキスト";
        var turkish = "İstanbul ığüşöç";
        var unicode = "København 東京 €𝄞";
        // Row 1: explicit varchar parameters. The client encodes them with the code page of the connection's database
        // collation (master: SQL_Latin1_General_CP1_CI_AS, 1252), not the column's, so only Latin text survives.
        // Row 3: nvarchar parameters; the server converts to each column's code page.
        foreach (var (id, type) in new[] { (1, System.Data.SqlDbType.VarChar), (3, System.Data.SqlDbType.NVarChar) })
        {
            await using var insert = new SqlCommand($"INSERT #g VALUES ({id}, @l, @j, @t, @u)", connection);
            insert.Parameters.Add(new SqlParameter("@l", type, 40) { Value = latin });
            insert.Parameters.Add(new SqlParameter("@j", type, 40) { Value = japanese });
            insert.Parameters.Add(new SqlParameter("@t", type, 40) { Value = turkish });
            insert.Parameters.Add(new SqlParameter("@u", System.Data.SqlDbType.NVarChar, 40) { Value = unicode });
            await insert.ExecuteNonQueryAsync();
        }

        // Row 2: literals converted by the server, read back through the column code pages (1252, 932, 1254).
        await Db.ExecAsync(connection,
            "INSERT #g VALUES (2, 'café Größe €', N'日本語テキスト', N'İstanbul ığüşöç', N'København 東京 €𝄞')");
        await using (var select = new SqlCommand(
            "SELECT Id, Latin, Japanese, Turkish, Unicode, DATALENGTH(Latin), DATALENGTH(Japanese), " +
            "DATALENGTH(Turkish) FROM #g ORDER BY Id", connection))
        await using (var reader = await select.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32(0);
                var ok = reader.GetString(1) == latin && reader.GetString(2) == japanese &&
                    reader.GetString(3) == turkish && reader.GetString(4) == unicode;
                failures += ok || id == 1 ? 0 : 1;
                Console.WriteLine($"{(ok ? "PASS" : id == 1 ? "INFO" : "FAIL")} globalization row={id} latin='{reader.GetString(1)}' " +
                    $"japanese='{reader.GetString(2)}' turkish='{reader.GetString(3)}' unicode='{reader.GetString(4)}' " +
                    $"bytes={reader.GetInt32(5)}/{reader.GetInt32(6)}/{reader.GetInt32(7)}");
            }
        }

        // A comparison the server evaluates with the collation, and a client comparison through ICU.
        var serverEqual = await Db.ScalarAsync<int>(connection,
            "SELECT CASE WHEN N'straße' = N'STRASSE' COLLATE Latin1_General_100_CI_AS THEN 1 ELSE 0 END");
        var clientEqual = string.Compare("straße", "STRASSE", CultureInfo.GetCultureInfo("de-DE"),
            CompareOptions.IgnoreCase) == 0;
        Console.WriteLine($"INFO globalization server straße=STRASSE (Latin1_General_100_CI_AS)={serverEqual} " +
            $"client de-DE IgnoreCase={clientEqual}");
        string codePage;
        try
        {
            codePage = Encoding.GetEncoding(932).WebName;
        }
        catch (NotSupportedException)
        {
            codePage = "unavailable";
        }

        Console.WriteLine($"INFO globalization Encoding.GetEncoding(932) after SqlClient use: {codePage}");
        Console.WriteLine($"RESULT globalization failures={failures}");
        return failures;
    }

    private static async Task<string> ConnectionInfoAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand(
            """
            SELECT c.net_transport, c.auth_scheme, c.encrypt_option, c.protocol_type,
                   CONVERT(varchar(10), CONVERT(binary(4), c.protocol_version), 2), s.client_interface_name,
                   s.client_version
            FROM sys.dm_exec_connections c JOIN sys.dm_exec_sessions s ON s.session_id = c.session_id
            WHERE c.session_id = @@SPID
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return $"transport={reader.GetString(0)} auth={reader.GetString(1)} encrypt={reader.GetString(2)} " +
            $"protocol={reader.GetString(3)} tds=0x{reader.GetString(4)} client='{reader.GetString(5)}' " +
            $"clientVersion={reader.GetInt32(6)} serverVersion={connection.ServerVersion}";
    }

    private static string HostOf(string connectionString)
    {
        var source = new SqlConnectionStringBuilder(connectionString).DataSource;
        var colon = source.IndexOf(':');
        if (colon >= 0)
        {
            source = source[(colon + 1)..];
        }

        var comma = source.IndexOf(',');
        return comma < 0 ? source : source[..comma];
    }

    // Exception chain, one level per inner exception, first line of each message.
    private static string Describe(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            var newline = message.IndexOfAny(['\r', '\n']);
            message = newline < 0 ? message : message[..newline];
            parts.Add(current is SqlException sql
                ? $"{current.GetType().Name}(Number={sql.Number}, Class={sql.Class}): {message}"
                : $"{current.GetType().Name}(HResult=0x{current.HResult:X8}): {message}");
        }

        return string.Join(" <- ", parts);
    }

    // Shared objects mapped into this process that matter for SqlClient on Linux.
    public static string MappedLibraries()
    {
        string[] interesting = ["ssl", "crypto", "icu", "gssapi", "krb5", "secret", "glib", "gio", "dbus", "System."];
        var libraries = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines("/proc/self/maps"))
        {
            var slash = line.IndexOf('/');
            if (slash < 0)
            {
                continue;
            }

            var name = Path.GetFileName(line[slash..]);
            if (name.Contains(".so", StringComparison.Ordinal) &&
                interesting.Any(i => name.Contains(i, StringComparison.Ordinal)))
            {
                libraries.Add(name);
            }
        }

        return libraries.Count == 0 ? "(none of interest)" : string.Join(' ', libraries);
    }
}

// libsecret through its non-variadic entry points ("v" variants), so no variadic call is imported.
internal static partial class SecretProbe
{
    private const string LibSecret = "libsecret-1.so.0";
    private const string LibGlib = "libglib-2.0.so.0";

    public static int Run()
    {
        Console.WriteLine($"SECRET DBUS_SESSION_BUS_ADDRESS={(Environment.GetEnvironmentVariable(
            "DBUS_SESSION_BUS_ADDRESS") is { Length: > 0 } ? "set" : "(unset)")} " +
            $"DISPLAY={Environment.GetEnvironmentVariable("DISPLAY") ?? "(unset)"}");
        nint glib;
        try
        {
            NativeLibrary.Load(LibSecret);
            glib = NativeLibrary.Load(LibGlib);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"FAIL secret load: {Db.Describe(exception)}");
            return 1;
        }

        var strHash = NativeLibrary.GetExport(glib, "g_str_hash");
        var strEqual = NativeLibrary.GetExport(glib, "g_str_equal");
        var free = NativeLibrary.GetExport(glib, "g_free");

        var types = g_hash_table_new_full(strHash, strEqual, free, 0);
        g_hash_table_insert(types, g_strdup("purpose"), 0); // SECRET_SCHEMA_ATTRIBUTE_STRING == 0
        g_hash_table_insert(types, g_strdup("server"), 0);
        var schema = secret_schema_newv("dev.spar.SqlTool.Probe", 0, types);
        g_hash_table_unref(types);

        var attributes = g_hash_table_new_full(strHash, strEqual, free, free);
        g_hash_table_insert(attributes, g_strdup("purpose"), g_strdup("spike"));
        g_hash_table_insert(attributes, g_strdup("server"), g_strdup("spar-sqllinux-a"));
        var failures = 0;
        try
        {
            var value = $"probe-{Guid.NewGuid():N}";
            var watch = Stopwatch.StartNew();
            var stored = secret_password_storev_sync(schema, attributes, "session", "Spar probe", value, 0, out var error);
            failures += Report("store", stored != 0, error, watch);

            watch.Restart();
            var found = secret_password_lookupv_sync(schema, attributes, 0, out error);
            var text = found == 0 ? null : Marshal.PtrToStringUTF8(found);
            if (found != 0)
            {
                secret_password_free(found);
            }

            failures += Report($"lookup matches={text == value}", text == value, error, watch);

            watch.Restart();
            var cleared = secret_password_clearv_sync(schema, attributes, 0, out error);
            failures += Report("clear", cleared != 0, error, watch);
        }
        finally
        {
            g_hash_table_unref(attributes);
            secret_schema_unref(schema);
        }

        Console.WriteLine($"RESULT secret failures={failures}");
        return failures;
    }

    private static int Report(string step, bool ok, nint error, Stopwatch watch)
    {
        var message = "";
        if (error != 0)
        {
            // GError: guint32 domain, gint code, gchar *message.
            message = $" code={Marshal.ReadInt32(error, 4)} message={Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8))}";
            g_error_free(error);
        }

        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} secret {step} ms={watch.Elapsed.TotalMilliseconds:F1}{message}");
        return ok ? 0 : 1;
    }

    [LibraryImport(LibGlib)]
    private static partial nint g_hash_table_new_full(nint hash, nint equal, nint keyDestroy, nint valueDestroy);

    [LibraryImport(LibGlib)]
    private static partial int g_hash_table_insert(nint table, nint key, nint value);

    [LibraryImport(LibGlib)]
    private static partial void g_hash_table_unref(nint table);

    [LibraryImport(LibGlib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint g_strdup(string text);

    [LibraryImport(LibGlib)]
    private static partial void g_error_free(nint error);

    [LibraryImport(LibSecret, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint secret_schema_newv(string name, int flags, nint attributeNamesAndTypes);

    [LibraryImport(LibSecret)]
    private static partial void secret_schema_unref(nint schema);

    [LibraryImport(LibSecret, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int secret_password_storev_sync(nint schema, nint attributes, string? collection,
        string label, string password, nint cancellable, out nint error);

    [LibraryImport(LibSecret)]
    private static partial nint secret_password_lookupv_sync(nint schema, nint attributes, nint cancellable,
        out nint error);

    [LibraryImport(LibSecret)]
    private static partial int secret_password_clearv_sync(nint schema, nint attributes, nint cancellable,
        out nint error);

    [LibraryImport(LibSecret)]
    private static partial void secret_password_free(nint password);
}
