using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Spar;

/// <summary>Marks services that reach a real resource: a database, a remote API, a file share.</summary>
public static class SparExternalServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TImplementation"/> as an external dependency. In a test host, resolving it without
    /// a replacement fails with a message that names it, so a test never reaches the real resource by accident.
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

/// <summary>Thrown in a test host when a test resolves an external dependency it neither replaced nor allowed.</summary>
public sealed class SparExternalDependencyException(Type service) : InvalidOperationException(
    $"{service.Name} is an external dependency (registered with AddExternal), and this test did not replace it. " +
    $"Replace it in the test's services, or allow the real one with options.AllowExternal<{service.Name}>().")
{
    public Type Service { get; } = service;
}

internal sealed record SparExternalMarker(ServiceDescriptor Original);

internal static class SparExternalGuard
{
    /// <summary>Swaps every external registration that the test neither replaced nor allowed for one that throws.</summary>
    public static void Apply(IServiceCollection services, IReadOnlySet<Type> allowed)
    {
        var markers = services
            .Where(descriptor => descriptor.ServiceType == typeof(SparExternalMarker))
            .Select(descriptor => (SparExternalMarker)descriptor.ImplementationInstance!)
            .ToList();

        foreach (var marker in markers)
        {
            var service = marker.Original.ServiceType;
            var last = services.LastOrDefault(descriptor => descriptor.ServiceType == service);
            var replaced = !ReferenceEquals(last, marker.Original);
            if (replaced || allowed.Contains(service))
            {
                continue;
            }

            services.Remove(marker.Original);
            services.Add(ServiceDescriptor.Describe(service, _ => throw new SparExternalDependencyException(service), marker.Original.Lifetime));
        }
    }
}
