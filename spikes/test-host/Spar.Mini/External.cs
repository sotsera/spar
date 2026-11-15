using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Spar;

/// <summary>Marks services that reach a real resource (a database, a remote API, a file share).</summary>
public static class SparExternalServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TImplementation"/> as an external dependency. In a test host, resolving it
    /// without a replacement fails with a message that names it, so a test never reaches the real resource by accident.
    /// </summary>
    public static IServiceCollection AddExternal<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
        this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TService : class
        where TImplementation : class, TService
    {
        var descriptor = ServiceDescriptor.Describe(typeof(TService), typeof(TImplementation), lifetime);
        services.Add(descriptor);
        services.Add(ServiceDescriptor.Singleton(new SparExternalMarker(descriptor)));
        return services;
    }
}

internal sealed record SparExternalMarker(ServiceDescriptor Original);

public sealed class SparExternalDependencyException(Type service) : InvalidOperationException(
    $"{service.Name} is an external dependency (registered with AddExternal), and this test did not replace it. " +
    $"Replace it in the test's services, or allow the real one with options.AllowExternal<{service.Name}>().")
{
    public Type Service { get; } = service;
}

internal static class SparExternalGuard
{
    /// <summary>Swaps every external registration that the test did not replace or allow for one that throws.</summary>
    public static void Apply(IServiceCollection services, IReadOnlySet<Type> allowed)
    {
        var markers = services.Where(d => d.ServiceType == typeof(SparExternalMarker))
            .Select(d => (SparExternalMarker)d.ImplementationInstance!)
            .ToList();
        foreach (var marker in markers)
        {
            var service = marker.Original.ServiceType;
            var last = services.LastOrDefault(d => d.ServiceType == service);
            if (!ReferenceEquals(last, marker.Original) || allowed.Contains(service))
            {
                continue;
            }

            services.Remove(marker.Original);
            services.Add(ServiceDescriptor.Describe(service, _ => throw new SparExternalDependencyException(service), marker.Original.Lifetime));
        }
    }
}
