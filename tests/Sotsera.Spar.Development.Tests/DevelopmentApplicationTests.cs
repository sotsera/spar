using System.IO.Pipes;
using AwesomeAssertions;
using Spar.NativeSample;
using Sotsera.Spar.Development;

namespace Sotsera.Spar.Development.Tests;

[CollectionDefinition("Application environment", DisableParallelization = true)]
public sealed class ApplicationEnvironmentTestsDefinition;

[Collection("Application environment")]
public sealed class DevelopmentApplicationTests
{
    [Fact]
    public async Task RunRejectsNonStaBeforeConstructingBackend()
    {
        var calls = 0;
        var result = StartThread(ApartmentState.MTA, () => DevelopmentApplication.Run(
            "Test", DevelopmentContract.GetFingerprint(typeof(ISampleBackend)), typeof(ISampleBackend), () =>
            {
                calls++;
                throw new InvalidOperationException("Factory must not run.");
            }));

        var error = await result;
        error.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("STA");
        calls.Should().Be(0);
    }

    [Theory]
    [InlineData("contract")]
    [InlineData("owner")]
    [InlineData("factory")]
    [InlineData("null-bridge")]
    public async Task RunVerifiesOwnerBeforeFactoryAndClosesConnectionOnFailure(string failure)
    {
        var pipeName = "spar-startup-test-" + Guid.NewGuid().ToString("N");
        var fingerprint = DevelopmentContract.GetFingerprint(typeof(ISampleBackend));
        var token = Guid.NewGuid().ToString("N");
        var session = Guid.NewGuid().ToString("N");
        var environment = new Dictionary<string, string>
        {
            ["SPAR_DEV_APP_PIPE"] = pipeName,
            ["SPAR_DEV_TOKEN"] = token,
            ["SPAR_DEV_SESSION"] = session,
            ["SPAR_DEV_BACKEND_OWNER"] = "debugger",
            // A connected owner must take precedence over inherited project-launch settings.
            ["SPAR_DEV_PROJECT"] = "not-a-project.json",
            ["SPAR_DEV_COORDINATOR"] = "not-a-coordinator.exe"
        };
        var previous = environment.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var connection = new SessionConnection(pipe);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var calls = 0;
        var factoryApartment = ApartmentState.Unknown;
        var callerThread = 0;
        var factoryThread = 0;
        Task<Exception?>? result = null;
        try
        {
            foreach (var entry in environment)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }

            result = StartThread(ApartmentState.STA, () =>
            {
                callerThread = Environment.CurrentManagedThreadId;
                DevelopmentApplication.Run("Test",
                    failure == "contract" ? new string('a', 64) : fingerprint, typeof(ISampleBackend), () =>
                    {
                        calls++;
                        factoryThread = Environment.CurrentManagedThreadId;
                        factoryApartment = Thread.CurrentThread.GetApartmentState();
                        if (failure == "null-bridge")
                        {
                            return null!;
                        }

                        throw new InvalidOperationException("Factory failed after admission.");
                    });
            });
            await pipe.WaitForConnectionAsync(timeout.Token);
            var hello = await connection.ReadAsync(timeout.Token);
            hello.Should().NotBeNull();
            hello!.Kind.Should().Be("app-hello");
            hello.Contract.Should().Be(fingerprint);
            await connection.WriteAsync(new SessionMessage("configure",
                Token: failure == "owner" ? "wrong-owner" : token, Session: session,
                Url: "http://127.0.0.1:12345/test/", Contract: fingerprint,
                Credential: new string('b', 64), Profile: Path.Combine(Path.GetTempPath(), session)), timeout.Token);

            // Actual EOF establishes that both rejected and admitted-but-failed startup release the pipe.
            var next = await connection.ReadAsync(timeout.Token);
            next.Should().BeNull();
            var error = await result;
            if (failure is "contract" or "owner")
            {
                error.Should().BeOfType<InvalidDataException>();
                calls.Should().Be(0);
            }
            else
            {
                error.Should().BeOfType<InvalidOperationException>();
                error!.Message.Should().Be(failure == "factory"
                    ? "Factory failed after admission." : "The bridge factory must return a bridge.");
                calls.Should().Be(1);
                factoryApartment.Should().Be(ApartmentState.STA);
                factoryThread.Should().Be(callerThread);
            }
        }
        finally
        {
            await connection.DisposeAsync();
            if (result is not null)
            {
                await result;
            }

            foreach (var entry in previous)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }
    }

    private static Task<Exception?> StartThread(ApartmentState apartment, Action action)
    {
        var completion = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                completion.SetResult(null);
            }
            catch (Exception error)
            {
                completion.SetResult(error);
            }
        });
        thread.SetApartmentState(apartment);
        thread.Start();
        return completion.Task;
    }
}
