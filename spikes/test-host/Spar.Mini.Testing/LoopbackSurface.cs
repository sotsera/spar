using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Spar.Testing;

/// <summary>
/// The packaged page and the bridge on 127.0.0.1 (a random port), inside the test process. Every bridge call needs
/// the per-app token, which the page reads from its own address.
/// </summary>
internal sealed class LoopbackSurface : IAsyncDisposable
{
    private readonly WebApplication _web;

    private LoopbackSurface(WebApplication web, string url)
    {
        _web = web;
        Url = url;
    }

    public string Url { get; }

    public static async Task<LoopbackSurface> StartAsync(SparRuntime runtime, CancellationToken cancellationToken)
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var web = builder.Build();

        web.MapPost("/_spar/call", async (HttpContext context) =>
        {
            if (!string.Equals(context.Request.Headers["X-Spar-Token"].ToString(), token, StringComparison.Ordinal))
            {
                context.Response.StatusCode = 401;
                return;
            }

            using var reader = new StreamReader(context.Request.Body);
            var message = await reader.ReadToEndAsync(context.RequestAborted);
            var reply = await runtime.Bridge.HandleAsync(message, context.RequestAborted);
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(reply, context.RequestAborted);
        });

        web.MapGet("/{**path}", async (HttpContext context) =>
        {
            var path = context.Request.Path.Value ?? "/";
            if (!runtime.Assets.TryGet(path, out var content))
            {
                context.Response.StatusCode = 404;
                return;
            }

            context.Response.ContentType = SparAssets.ContentType(path == "/" ? "/index.html" : path);
            context.Response.Headers.ContentSecurityPolicy = SparAssets.ContentSecurityPolicy;
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.Body.WriteAsync(content, context.RequestAborted);
        });

        await web.StartAsync(cancellationToken);
        var address = web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new LoopbackSurface(web, $"{address}/index.html?token={token}");
    }

    public async ValueTask DisposeAsync()
    {
        await _web.StopAsync();
        await _web.DisposeAsync();
    }
}
