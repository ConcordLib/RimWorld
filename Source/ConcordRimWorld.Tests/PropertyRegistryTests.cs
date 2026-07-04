using System;
using Xunit;

namespace Concord.RimWorld.Tests;

public sealed class PropertyRegistryTests {
    private sealed class Target { }

    [Fact]
    public void Add_ThenForBaseType_ReturnsEntry() {
        PropertyRegistry registry = new PropertyRegistry();
        registry.Add(typeof(Target), "count", typeof(int), null);

        System.Collections.Generic.IReadOnlyList<PropertyEntry> entries = registry.ForBaseType(typeof(Target));

        Assert.Single(entries);
        Assert.Equal("count", entries[0].Key);
        Assert.Equal("concord.count", entries[0].ScribeLabel);
    }

    [Fact]
    public void Add_DuplicateKey_Throws() {
        PropertyRegistry registry = new PropertyRegistry();
        registry.Add(typeof(Target), "count", typeof(int), null);

        Assert.Throws<InvalidOperationException>(() => registry.Add(typeof(Target), "count", typeof(int), null));
    }

    [Fact]
    public void Add_BclType_Throws() {
        PropertyRegistry registry = new PropertyRegistry();

        Assert.Throws<ArgumentException>(() => registry.Add(typeof(string), "x", typeof(int), null));
    }

    [Fact]
    public void Add_UnsupportedValueType_Throws() {
        PropertyRegistry registry = new PropertyRegistry();

        Assert.Throws<ArgumentException>(() => registry.Add(typeof(Target), "x", typeof(double), null));
    }

    [Fact]
    public void IsEmpty_TrueUntilAdded() {
        PropertyRegistry registry = new PropertyRegistry();
        Assert.True(registry.IsEmpty);
        registry.Add(typeof(Target), "count", typeof(int), null);
        Assert.False(registry.IsEmpty);
    }
}
