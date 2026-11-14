using System.Buffers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Sample.App;
using Sample.Contracts;
using Spar;
using Spar.Generated;

// Exercises every contract form and command shape through the generated dispatch, on CoreCLR and NativeAOT.
var services = new ServiceCollection();
SparCatalog.AddSparContracts(services);
services.AddScoped<CallScope>();
services.AddScoped<IBillingApi, BillingApi>();
services.AddScoped<INotesService, NotesService>();
services.AddScoped<IPricingService, Pricing>();
await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

var failures = 0;

async Task Unary(string contract, string command, string payload, string expected)
{
    var bytes = await SparCatalog.InvokeAsync(provider, contract, command, Encoding.UTF8.GetBytes(payload), CancellationToken.None);
    Check($"{contract}.{command}", Encoding.UTF8.GetString(bytes), expected);
}

async Task Stream(string contract, string command, string payload, string expected)
{
    await using var stream = SparCatalog.OpenStream(provider, contract, command, Encoding.UTF8.GetBytes(payload), CancellationToken.None);
    var batches = new List<string>();
    var buffer = new ArrayBufferWriter<byte>();
    while (true)
    {
        buffer.ResetWrittenCount();
        var batch = await stream.ReadBatchAsync(buffer, 2, CancellationToken.None);
        if (batch.Count == 0)
        {
            break;
        }

        batches.Add(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    Check($"{contract}.{command} (stream)", string.Join(" ", batches), expected);
}

async Task Rejected<TException>(string contract, string command, string payload)
    where TException : Exception
{
    try
    {
        await SparCatalog.InvokeAsync(provider, contract, command, Encoding.UTF8.GetBytes(payload), CancellationToken.None);
        Check($"{contract}.{command} rejects {payload}", "accepted", typeof(TException).Name);
    }
    catch (TException exception)
    {
        Check($"{contract}.{command} rejects {payload}", exception.GetType().Name, typeof(TException).Name);
    }
}

void Check(string label, string actual, string expected)
{
    if (actual == expected)
    {
        Console.WriteLine($"PASS {label}: {actual}");
        return;
    }

    failures++;
    Console.WriteLine($"FAIL {label}\n  expected {expected}\n  actual   {actual}");
}

// Class form (local): scalar, none, Result with every case, Guid, explicit name, referenced types, renamed type.
await Unary("planning", "get", "\"p1\"",
    """{"id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","name":"p1","steps":[{"order":1,"title":"scan"}],"budget":{"amount":12,"currency":"EUR"}}""");
await Unary("planning", "build", """{"connection":"main","table":"orders","force":false}""",
    """{"ok":true,"value":{"id":"00000000-0000-0000-0000-000000000000","name":"orders","steps":[],"budget":null}}""");
await Unary("planning", "build", """{"connection":"other","table":"orders","force":false}""",
    """{"ok":false,"error":{"code":"connectionNotFound","connectionId":"other"}}""");
await Unary("planning", "build", """{"connection":"main","table":"","force":false}""",
    """{"ok":false,"error":{"code":"tableMissing","connection":"main","table":""}}""");
await Unary("planning", "build", """{"connection":"main","table":"secret","force":false}""",
    """{"ok":false,"error":{"code":"forbidden"}}""");
await Unary("planning", "total", "\"6f9619ff-8b86-d011-b42d-00c04fc964ff\"", """{"amount":42,"currency":"EUR"}""");
await Unary("planning", "lookup", "\"A-1\"", """{"ok":true,"value":{"sku":"A-1","quantity":1,"price":null}}""");
await Unary("planning", "lookup", "\"Z-9\"", """{"ok":false,"error":{"code":"notFound","sku":"Z-9"}}""");
await Unary("planning", "items", "", """[{"label":"box","weight":1.5}]""");
await Stream("planning", "steps", "3",
    """[{"order":1,"title":"step 1 of call 10"},{"order":2,"title":"step 2 of call 10"}] [{"order":3,"title":"step 3 of call 10"}]""");

// One contract instance and one scope per call.
var before = PlanningService.Instances;
await Unary("planning", "count", "", "11");
await Unary("planning", "count", "", "12");
Check("planning instances per call", (PlanningService.Instances - before).ToString(), "2");

// Interface forms (local): explicit and derived names; a case code renamed with [SparName].
await Unary("billing", "charge", """{"amount":1,"currency":"EUR"}""", """{"amount":2,"currency":"EUR"}""");
await Unary("billing", "pay", """{"amount":{"amount":5,"currency":"EUR"},"account":"a"}""",
    """{"ok":true,"value":{"id":"00000000-0000-0000-0000-000000000001","charged":{"amount":5,"currency":"EUR"}}}""");
await Unary("billing", "pay", """{"amount":{"amount":150,"currency":"EUR"},"account":"a"}""",
    """{"ok":false,"error":{"code":"insufficientFunds","missing":{"amount":50,"currency":"EUR"}}}""");
await Unary("billing", "pay", """{"amount":{"amount":5,"currency":"EUR"},"account":"locked"}""",
    """{"ok":false,"error":{"code":"accountLocked","account":"locked"}}""");
await Unary("notes", "echo", "\"hello\"", "\"hello\"");

// Class and interface forms from the referenced assembly.
await Unary("inventory", "get", "\"A-1\"", """{"sku":"A-1","quantity":3,"price":{"amount":9.5,"currency":"EUR"}}""");
await Unary("inventory", "find", "\"OLD-1\"", """{"ok":false,"error":{"code":"discontinued","sku":"OLD-1","replacement":"A-1"}}""");
await Unary("inventory", "find", "\"B-2\"", """{"ok":true,"value":{"sku":"B-2","quantity":0,"price":null}}""");
await Unary("pricing", "quote", """{"sku":"A-1","quantity":4}""", """{"amount":10,"currency":"EUR"}""");

// Requests that never reach the service.
await Rejected<SparInvalidRequestException>("planning", "get", "42");
await Rejected<SparInvalidRequestException>("planning", "get", "\"a\" \"b\"");
await Rejected<SparInvalidRequestException>("planning", "build", """{"connection":"main"}""");
await Rejected<SparUnknownCommandException>("planning", "nope", "");
await Rejected<SparUnknownCommandException>("nope", "get", "");

Console.WriteLine($"manifest: {SparCatalog.Manifest.Length} bytes");
Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
