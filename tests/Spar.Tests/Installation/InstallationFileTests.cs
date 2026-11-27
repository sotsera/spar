namespace Spar.Tests.Installation;

/// <summary>
/// The installation file (decision "M3b's decide-first questions", item 6): read only when there is one, deployment
/// choices only, environment variables expanded, and every unknown key, wrong value or locked key refused by name.
/// </summary>
public sealed class InstallationFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(TestFolders.Artifacts(), "installation-tests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, "SqlTool.spar.json");

    [Fact]
    public void Without_a_file_there_is_nothing_to_apply()
    {
        Assert.Null(InstallationFile.Read(FilePath, new InstallationOptions()));
    }

    [Fact]
    public void A_file_sets_the_folders_and_each_settings_records_place_with_variables_expanded()
    {
        var path = OperatingSystem.IsWindows() ? @"\\fileserver\tools\%SPAR_TEST_USER%.json" : "//fileserver/tools/%SPAR_TEST_USER%.json";
        var share = System.Text.Json.JsonEncodedText.Encode(path).ToString();
        Environment.SetEnvironmentVariable("SPAR_TEST_USER", "ada");
        Write($$"""
            {
              "folders": { "ephemeralBrowserData": true, "removeOnExit": ["profile", "cache", "data"] },
              "settings": { "file": "{{share}}", "allowNetworkFile": true },
              "settings.layout": { "allowNetworkFile": false }
            }
            """);

        var file = InstallationFile.Read(FilePath, new InstallationOptions())!;
        var folders = new FolderOptions();
        file.ApplyTo(folders);

        Assert.True(folders.BrowserDataInMemory);
        Assert.Equal([SparFolder.Profile, SparFolder.Cache, SparFolder.Data], folders.RemovedOnExit);
        Assert.EndsWith("ada.json", file.Settings["settings"].File, StringComparison.Ordinal);
        Assert.True(file.Settings["settings"].AllowNetworkFile);
        Assert.False(file.Settings["layout"].AllowNetworkFile);
        Assert.Contains("settings.file", file.Keys);
    }

    [Fact]
    public void A_file_sets_the_log_levels_over_the_codes()
    {
        Write("""{ "logging": { "level": "warning", "categories": { "SqlTool.Copy": "Debug" } } }""");
        var logging = new LoggingOptions().Level(Microsoft.Extensions.Logging.LogLevel.Information);

        var file = InstallationFile.Read(FilePath, new InstallationOptions())!;
        file.ApplyTo(logging);

        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, logging.DefaultLevel);
        Assert.Contains(logging.Categories, rule => rule.Category == "SqlTool.Copy" && rule.Level == Microsoft.Extensions.Logging.LogLevel.Debug);
        Assert.Equal("logging.level = Warning; logging.categories.SqlTool.Copy = Debug", file.Describe());
    }

    [Theory]
    [InlineData("""{ "logging": { "level": "loud" } }""", "'logging.level' must be one of")]
    [InlineData("""{ "logging": { "file": false } }""", "'logging.file' is not a key")]
    [InlineData("""{ "browser": { "devtools": true } }""", "'browser' is not a key")]
    [InlineData("""{ "folders": { "temp": "/tmp/x" } }""", "'folders.temp' is not a key")]
    [InlineData("""{ "folders": { "ephemeralBrowserData": "yes" } }""", "'folders.ephemeralBrowserData' must be true or false")]
    [InlineData("""{ "folders": { "removeOnExit": ["everything"] } }""", "lists 'everything'")]
    [InlineData("""{ "settings": { "file": "settings.json" } }""", "must be an absolute path")]
    [InlineData("""{ "folders": """, "not valid JSON")]
    public void A_file_that_cannot_be_used_stops_the_start_and_names_the_key(string json, string reason)
    {
        Write(json);

        var error = Assert.Throws<InvalidOperationException>(() => InstallationFile.Read(FilePath, new InstallationOptions()));

        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
        Assert.Contains(FilePath, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_the_code_locked_is_refused()
    {
        Write("""{ "folders": { "removeOnExit": ["logs"] } }""");

        var error = Assert.Throws<InvalidOperationException>(
            () => InstallationFile.Read(FilePath, new InstallationOptions().Locked("folders.removeOnExit")));

        Assert.Contains("'folders.removeOnExit', which this app does not let an installation change", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_known_keys_can_be_locked()
    {
        Assert.Throws<ArgumentException>(() => new InstallationOptions().Locked("browser.devtools"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SPAR_TEST_USER", null);
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private void Write(string json)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, json);
    }
}
