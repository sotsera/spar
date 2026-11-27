using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Spar.Runtime;
using Spar.Settings;

namespace Spar.Tests.Settings;

/// <summary>
/// Settings files (decision "M3b's decide-first questions", item 4), against a hand-written serializer standing in for the
/// generated one: defaults, writes and reads, properties missing or unknown, migrations, a file set aside, a newer
/// version's file, the Development name, and a file on a share that cannot be reached or that another machine wrote.
/// </summary>
public sealed class SettingsTests : IDisposable
{
    private static readonly Prefs Defaults = new("System", 50_000, ["a"]);

    private readonly string _root = Path.Combine(TestFolders.Artifacts(), "settings-tests", Guid.NewGuid().ToString("N"));
    private readonly CapturedLog _log = new();

    private SparFolders Folders => SparFolders.ForTest(_root);

    private string DefaultFile => Path.Combine(Folders.Data, "settings.json");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_a_file_the_defaults_apply_and_the_first_update_writes_one()
    {
        using var settings = Load(new SettingsOptions());
        Assert.Equal((Defaults, SparSettingsSource.Defaults), (settings.Current, settings.Source));

        var updated = await settings.UpdateAsync(p => p with { BatchSize = 1000 }, Token);

        Assert.Equal(1000, updated.BatchSize);
        Assert.Equal(SparSettingsSource.File, settings.Source);
        using var file = JsonDocument.Parse(File.ReadAllText(DefaultFile));
        Assert.Equal(1, file.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(1000, file.RootElement.GetProperty("settings").GetProperty("batchSize").GetInt32());
        Assert.Empty(Directory.GetFiles(Folders.Data, "*.tmp-*"));
    }

    [Fact]
    public void A_missing_property_keeps_its_default_and_an_unknown_one_is_dropped_with_a_log_line()
    {
        Seed(DefaultFile, """{"version":1,"settings":{"theme":"Dark","retired":true}}""");

        using var settings = Load(new SettingsOptions());

        Assert.Equal(new Prefs("Dark", 50_000, Defaults.Recent), settings.Current);
        Assert.Contains(_log.Entries, entry => entry.Message.Contains("retired", StringComparison.Ordinal));
    }

    [Fact]
    public void An_older_file_goes_through_its_migrations()
    {
        Seed(DefaultFile, """{"version":1,"settings":{"colour":"Dark"}}""");
        var options = new SettingsOptions().Version(2).Migrate(1, json =>
        {
            json["theme"] = json["colour"]?.DeepClone();
            json.Remove("colour");
            return json;
        });

        using var settings = Load(options);

        Assert.Equal("Dark", settings.Current.Theme);
        Assert.Equal(SparSettingsSource.File, settings.Source);
    }

    [Fact]
    public void An_unreadable_file_is_set_aside_and_the_app_starts_on_the_defaults()
    {
        Seed(DefaultFile, """{"version":1,"settings":{"batchSize":"many"}}""");

        using var settings = Load(new SettingsOptions());

        Assert.Equal((Defaults, SparSettingsSource.Defaults), (settings.Current, settings.Source));
        Assert.False(File.Exists(DefaultFile));
        Assert.Single(Directory.GetFiles(Folders.Data, "settings.json.bad-*"));
        Assert.Contains(_log.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task A_file_a_newer_version_wrote_is_left_alone_and_updates_fail()
    {
        Seed(DefaultFile, """{"version":3,"settings":{"theme":"Dark"}}""");

        using var settings = Load(new SettingsOptions());

        Assert.Equal((Defaults, SparSettingsSource.NewerVersion), (settings.Current, settings.Source));
        await Assert.ThrowsAsync<SparSettingsUnavailableException>(() => settings.UpdateAsync(p => p, Token));
        Assert.Contains("\"version\":3", File.ReadAllText(DefaultFile), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_named_record_has_its_own_file_and_a_Development_build_its_own_name()
    {
        using var named = Load(new SettingsOptions().Name("layout"));
        await named.UpdateAsync(p => p with { Theme = "Light" }, Token);
        var chosen = Path.Combine(_root, "elsewhere", "prefs.json");
        using var development = Load(new SettingsOptions().File(chosen), development: true);
        await development.UpdateAsync(p => p with { Theme = "Light" }, Token);

        Assert.True(File.Exists(Path.Combine(Folders.Data, "layout.json")));
        Assert.True(File.Exists(Path.Combine(_root, "elsewhere", "prefs.Development.json")));
        Assert.False(File.Exists(chosen));
    }

    [Fact]
    public void A_file_on_a_share_needs_AllowNetworkFile()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Load(new SettingsOptions().File(Shared), network: true));

        Assert.Contains("AllowNetworkFile", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreachable_share_starts_on_the_local_copy_read_only()
    {
        var options = new SettingsOptions().File(Shared).AllowNetworkFile();
        using (var first = Load(options, network: true))
        {
            await first.UpdateAsync(p => p with { Theme = "Dark" }, Token);
        }

        // Held open with no sharing, the file reads as a share that cannot be reached.
        using (new FileStream(Shared, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            using var settings = Load(options, network: true);

            Assert.Equal(("Dark", SparSettingsSource.LocalCopy), (settings.Current.Theme, settings.Source));
            await Assert.ThrowsAsync<SparSettingsUnavailableException>(() => settings.UpdateAsync(p => p, Token));
            Assert.Contains(_log.Entries, entry => entry.Message.Contains("cannot be reached", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task An_update_applies_to_what_another_machine_wrote_meanwhile()
    {
        var options = new SettingsOptions().File(Shared).AllowNetworkFile();
        using var here = Load(options, network: true);
        await here.UpdateAsync(p => p with { Theme = "Dark" }, Token);

        // Another machine changes the batch size behind this one's back.
        Seed(Shared, """{"version":1,"settings":{"theme":"Dark","batchSize":7,"recent":["a"]}}""");
        var updated = await here.UpdateAsync(p => p with { Recent = [.. p.Recent, "b"] }, Token);

        Assert.Equal(new Prefs("Dark", 7, ["a", "b"]), updated);
    }

    [Fact]
    public async Task Watchers_get_the_current_settings_then_each_update()
    {
        using var settings = Load(new SettingsOptions());
        await using var watch = settings.WatchAsync(Token).GetAsyncEnumerator(Token);
        Assert.True(await watch.MoveNextAsync());
        Assert.Equal(Defaults, watch.Current);

        await settings.UpdateAsync(p => p with { Theme = "Dark" }, Token);

        Assert.True(await watch.MoveNextAsync());
        Assert.Equal("Dark", watch.Current.Theme);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Shared => Path.Combine(_root, "share", "prefs.json");

    private SparSettings<Prefs> Load(SettingsOptions options, bool development = false, bool? network = null) =>
        SparSettings<Prefs>.Load(
            Defaults, options, Prefs.Serializer, Folders, _log.CreateLogger("Spar.Settings"), TimeProvider.System, test: false, development, network);

    private static void Seed(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    /// <summary>A settings record, with the reader and writer the generator would write.</summary>
    private sealed record Prefs(string Theme, int BatchSize, IReadOnlyList<string> Recent)
    {
        public static SparSettingsSerializer<Prefs> Serializer { get; } = new(Read, Write);

        public bool Equals(Prefs? other) =>
            other is not null && Theme == other.Theme && BatchSize == other.BatchSize && Recent.SequenceEqual(other.Recent);

        public override int GetHashCode() => HashCode.Combine(Theme, BatchSize);

        private static Prefs Read(ref Utf8JsonReader reader)
        {
            SparJson.Expect(ref reader, JsonTokenType.StartObject);
            string? theme = null;
            int? batchSize = null;
            List<string>? recent = null;
            while (SparJson.NextProperty(ref reader))
            {
                if (reader.ValueTextEquals("theme"u8))
                {
                    reader.Read();
                    theme = SparJson.ReadString(ref reader);
                }
                else if (reader.ValueTextEquals("batchSize"u8))
                {
                    reader.Read();
                    batchSize = SparJson.ReadInt32(ref reader);
                }
                else if (reader.ValueTextEquals("recent"u8))
                {
                    reader.Read();
                    SparJson.Expect(ref reader, JsonTokenType.StartArray);
                    recent = [];
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        recent.Add(SparJson.ReadString(ref reader));
                    }
                }
                else
                {
                    SparJson.SkipValue(ref reader);
                }
            }

            return new Prefs(
                theme ?? throw SparJson.Missing(ref reader, "theme"),
                batchSize ?? throw SparJson.Missing(ref reader, "batchSize"),
                recent ?? throw SparJson.Missing(ref reader, "recent"));
        }

        private static void Write(Utf8JsonWriter writer, Prefs value)
        {
            writer.WriteStartObject();
            writer.WriteString("theme"u8, value.Theme);
            writer.WriteNumber("batchSize"u8, value.BatchSize);
            writer.WriteStartArray("recent"u8);
            foreach (var item in value.Recent)
            {
                writer.WriteStringValue(item);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
