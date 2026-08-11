using Microsoft.Extensions.DependencyInjection;
using SPTarkov.DI;
using SPTarkov.DI.Annotations;
using Xunit;

namespace BehavioralTests;

// Characterization of DependencyInjectionHandler (4.1.2). Mods observe every one of
// these behaviors: lifetimes, interface/base registration, and TypePriority ordering
// (the server loads IOnLoad implementors in registration order).
public class DependencyInjectionHandlerTests
{
    public interface IMarkerService
    {
        string Name { get; }
    }

    // Names deliberately oppose priority: alphabetically Alpha < Zebra, by priority
    // Zebra(100) < Alpha(200). A type-name sort would produce ["late", "early"], so the
    // ordering test below actually distinguishes TypePriority from incidental name order.
    [Injectable(InjectionType.Singleton, typePriority: 100)]
    public class ZebraService : IMarkerService
    {
        public string Name => "early";
    }

    [Injectable(InjectionType.Singleton, typePriority: 200)]
    public class AlphaService : IMarkerService
    {
        public string Name => "late";
    }

    [Injectable]
    public class TransientThing;

    [Injectable(InjectionType.Singleton)]
    public class SingletonThing;

    public abstract class MarkerBase;

    [Injectable(InjectionType.Singleton)]
    public class DerivedFromBase : MarkerBase;

    private static ServiceProvider Build(params Type[] types)
    {
        var services = new ServiceCollection();
        var handler = new DependencyInjectionHandler(services);
        handler.AddInjectableTypesFromTypeList(types);
        handler.InjectAll();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void SingletonResolvesTheSameInstance()
    {
        using var provider = Build(typeof(SingletonThing));
        Assert.Same(provider.GetRequiredService<SingletonThing>(), provider.GetRequiredService<SingletonThing>());
    }

    [Fact]
    public void TransientResolvesDistinctInstances()
    {
        using var provider = Build(typeof(TransientThing));
        Assert.NotSame(provider.GetRequiredService<TransientThing>(), provider.GetRequiredService<TransientThing>());
    }

    [Fact]
    public void InjectableTypeIsResolvableByItsInterface()
    {
        using var provider = Build(typeof(ZebraService));
        Assert.IsType<ZebraService>(provider.GetRequiredService<IMarkerService>());
    }

    [Fact]
    public void InterfaceAndConcreteResolutionsShareTheSingletonInstance()
    {
        using var provider = Build(typeof(ZebraService));
        Assert.Same(provider.GetRequiredService<ZebraService>(), provider.GetRequiredService<IMarkerService>());
    }

    [Fact]
    public void InjectableTypeIsResolvableByItsBaseClass()
    {
        using var provider = Build(typeof(DerivedFromBase));
        Assert.IsType<DerivedFromBase>(provider.GetRequiredService<MarkerBase>());
    }

    [Fact]
    public void GetServicesOrderFollowsTypePriorityAscending()
    {
        // InjectAll sorts registrations by TypePriority, and MEDI preserves
        // registration order in GetServices — this is the load-order contract.
        // Registration order here is reversed relative to the expected output.
        using var provider = Build(typeof(AlphaService), typeof(ZebraService));
        var names = provider.GetServices<IMarkerService>().Select(s => s.Name).ToList();
        Assert.Equal(["early", "late"], names);
    }

    [Fact]
    public void InjectAllIsOneTimeUse()
    {
        var services = new ServiceCollection();
        var handler = new DependencyInjectionHandler(services);
        handler.AddInjectableTypesFromTypeList([typeof(SingletonThing)]);
        handler.InjectAll();
        var ex = Assert.Throws<Exception>(handler.InjectAll);
        Assert.Contains("one time use", ex.Message);
    }
}
