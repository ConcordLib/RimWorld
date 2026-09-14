using System;
using Concord.AttachedData;
using Xunit;

namespace Concord.RimWorld.Tests;

public sealed class RimWorldAttachedPropertyRegistryTests {
    private sealed class Target { }

    [Fact]
    public void RegisterAttachedProperty_NamespacesKeyWithDeclaringAssembly() {
        PropertyRegistry registry = new PropertyRegistry();
        RimWorldAttachedPropertyRegistry adapter = new RimWorldAttachedPropertyRegistry(registry);

        IAttachedSlot slot = AttachedStorage.SlotAt(AttachedStorage.SlotFor(typeof(Target), "count", typeof(int)));
        adapter.RegisterAttachedProperty(typeof(Target), typeof(Verse.Thing), "count", typeof(int), slot);

        System.Collections.Generic.IReadOnlyList<PropertyEntry> entries = registry.ForBaseType(typeof(Verse.Thing));
        Assert.Single(entries);
        Assert.Equal(typeof(Target).Assembly.GetName().Name + ".count", entries[0].Key);
    }
}
