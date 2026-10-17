using System.Reflection;
using Yolk.Application.Abstractions;
using Yolk.Application.Features.Status;
using Yolk.Domain.Configuration;
using Yolk.Infrastructure;
using Yolk.TestSupport;

namespace Yolk.Architecture.Tests;

/// <summary>
/// The rules of the Windows side's Clean Architecture, checked by reflection.
/// Project references already stop an inner layer referencing an outer one;
/// these cover what references can't.
/// </summary>
public class LayeringTests
{
    private static readonly Assembly Domain = typeof(YolkConfig).Assembly;
    private static readonly Assembly Application = typeof(GetStatus).Assembly;
    private static readonly Assembly Infrastructure = typeof(DependencyInjection).Assembly;
    private static readonly Assembly Cli = Assembly.Load("yolk");

    private static readonly Assembly[] All = [Domain, Application, Infrastructure, Cli];

    private static IEnumerable<string> YolkReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("Yolk", StringComparison.Ordinal) || name == "yolk");

    [Fact]
    public void TheDomainDependsOnNothing()
    {
        Assert.Empty(YolkReferences(Domain));
        Assert.DoesNotContain(Domain.GetReferencedAssemblies(),
            a => a.Name!.StartsWith("Microsoft.Extensions", StringComparison.Ordinal));
    }

    [Fact]
    public void TheApplicationDependsOnlyOnTheDomain() =>
        Assert.Equal(["Yolk.Domain"], YolkReferences(Application));

    [Fact]
    public void InfrastructureDependsOnlyInward() =>
        Assert.Equal(["Yolk.Application", "Yolk.Domain"], YolkReferences(Infrastructure).Order());

    /// <summary>
    /// Every Win32 call goes through Infrastructure. A P/Invoke anywhere else
    /// is OS code leaking into a layer that should not know about Windows.
    /// </summary>
    [Fact]
    public void OnlyInfrastructureCallsNativeCode()
    {
        // The check itself works: Infrastructure's native calls are found.
        Assert.NotEmpty(NativeMethods(Infrastructure));

        foreach (var assembly in All.Where(a => a != Infrastructure))
        {
            Assert.Empty(NativeMethods(assembly));
        }
    }

    private static IEnumerable<string> NativeMethods(Assembly assembly) =>
        assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => m.Attributes.HasFlag(MethodAttributes.PinvokeImpl))
            .Select(m => $"{m.DeclaringType!.FullName}.{m.Name}");

    /// <summary>Each use case is a slice: its handler lives under Features/.</summary>
    [Fact]
    public void EveryHandlerLivesInAFeatureSlice()
    {
        var misplaced = Handlers().Where(t => t.Namespace?.StartsWith("Yolk.Application.Features.", StringComparison.Ordinal) != true);
        Assert.Empty(misplaced.Select(t => t.FullName));
    }

    /// <summary>
    /// AddApplication lists its handlers explicitly (no reflection, for
    /// NativeAOT), so a new slice that forgets to register itself fails here
    /// rather than at runtime.
    /// </summary>
    [Fact]
    public void EveryHandlerIsRegistered()
    {
        using var host = new TestHost();
        var handlerInterfaces = Handlers().SelectMany(HandlerInterfaces).ToList();

        Assert.NotEmpty(handlerInterfaces);
        foreach (var contract in handlerInterfaces)
        {
            var resolved = host.Get(contract);
            Assert.True(resolved is not null, $"{contract} is not registered in AddApplication");
        }
    }

    /// <summary>The adapters are internal: the outside world sees ports, not Win32 classes.</summary>
    [Fact]
    public void InfrastructureExposesOnlyItsRegistration() =>
        Assert.Equal([typeof(DependencyInjection)], Infrastructure.GetExportedTypes());

    private static IEnumerable<Type> Handlers() =>
        Application.GetTypes().Where(t => t.IsClass && !t.IsAbstract && HandlerInterfaces(t).Any());

    private static IEnumerable<Type> HandlerInterfaces(Type type) =>
        type.GetInterfaces().Where(i => i.IsGenericType && (
            i.GetGenericTypeDefinition() == typeof(ICommandHandler<>)
            || i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
            || i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)));
}
