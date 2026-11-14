using System.Diagnostics;
using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Notes.IntegrationTests;

public sealed class NotesWithSqlServerTests
{
    [Fact]
    public async Task A_note_saved_in_the_ui_lands_in_sql_server()
    {
        var token = TestContext.Current.CancellationToken;
        var timer = Stopwatch.StartNew();
        var marks = new List<string>();
        void Mark(string name) => marks.Add($"{name} {timer.Elapsed.TotalSeconds:F1}s");

        var bridgeToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Notes_AppHost>([$"Parameters:spar-token={bridgeToken}"], token);
        await using var app = await builder.BuildAsync(token);
        await app.StartAsync(token);
        Mark("started");

        // Keep every resource's console output, written to artifacts/aspire/ at the end (diagnosis).
        var resourceLogs = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var loggers = app.Services.GetRequiredService<ResourceLoggerService>();
        foreach (var name in (string[])["notes-app", "frontend"])
        {
            _ = Task.Run(async () =>
            {
                await foreach (var batch in loggers.WatchAsync(name).WithCancellation(token))
                {
                    foreach (var line in batch)
                    {
                        resourceLogs.Enqueue($"{name}: {line.Content}");
                    }
                }
            }, CancellationToken.None);
        }

        try
        {

        var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
        await notifications.WaitForResourceHealthyAsync("notes", token).WaitAsync(TimeSpan.FromMinutes(3), token);
        Mark("sql-healthy");
        await notifications.WaitForResourceAsync("notes-app", KnownResourceStates.Running, token).WaitAsync(TimeSpan.FromMinutes(2), token);
        await notifications.WaitForResourceAsync("frontend", KnownResourceStates.Running, token).WaitAsync(TimeSpan.FromMinutes(2), token);
        var frontend = app.GetEndpoint("frontend", "http");
        await WaitForHttpAsync(frontend, token);
        Mark("frontend-serving");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(frontend.ToString());
        await Expect(page.GetByLabel("Note count")).ToHaveTextAsync("0 notes", new() { Timeout = 30000 });
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Note" }).FillAsync("Buy milk");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Tags" }).FillAsync("shopping");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Listitem)).ToContainTextAsync("Buy milk #shopping");
        Mark("ui-journey");

        // The row is really in SQL Server: read it back with the connection string Aspire gave the app.
        await using var connection = new SqlConnection(await app.GetConnectionStringAsync("notes", token));
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("SELECT Text, Tags FROM dbo.Notes", connection);
        await using var reader = await command.ExecuteReaderAsync(token);
        Assert.True(await reader.ReadAsync(token));
        Assert.Equal("Buy milk", reader.GetString(0));
        Assert.Equal("shopping", reader.GetString(1));
        Assert.False(await reader.ReadAsync(token));
        Mark("sql-verified");

        }
        finally
        {
            Record(string.Join(", ", marks));
            File.WriteAllLines(Path.Combine(ArtifactsFolder(), "resource-logs.txt"), resourceLogs);
        }
    }

    private static async Task WaitForHttpAsync(Uri url, CancellationToken token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var attempt = 0; attempt < 120; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(url, token);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!token.IsCancellationRequested)
            {
            }

            await Task.Delay(250, token);
        }

        throw new TimeoutException($"{url} did not answer.");
    }

    private static string ArtifactsFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        var folder = Path.Combine(directory!.FullName, "artifacts", "aspire");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Record(string line)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        var folder = Path.Combine(directory!.FullName, "artifacts", "aspire");
        Directory.CreateDirectory(folder);
        File.AppendAllLines(Path.Combine(folder, "timings.txt"), [$"{DateTime.Now:HH:mm:ss} {line}"]);
    }
}
