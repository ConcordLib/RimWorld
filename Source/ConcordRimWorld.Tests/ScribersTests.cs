using System;
using System.Collections.Generic;
using Verse;
using Xunit;

namespace Concord.RimWorld.Tests;

[Collection(GameAssemblies.Name)]
public sealed class ScribersTests {
    private sealed class Holder : IExposable {
        public void ExposeData() {
            SaveHooks.ScribeAttached(this);
        }
    }

    private sealed class OtherHolder : IExposable {
        public void ExposeData() {
            SaveHooks.ScribeAttached(this);
        }
    }

    [Fact]
    public void Add_SameNameOnTwoTargets_GetsDistinctLabels() {
        PropertyRegistry registry = new PropertyRegistry();
        registry.Add(typeof(Holder), "Probe.count", typeof(int), null);
        registry.Add(typeof(OtherHolder), "Probe.count", typeof(int), null);

        IReadOnlyList<PropertyEntry> first = registry.ForBaseType(typeof(Holder));
        IReadOnlyList<PropertyEntry> second = registry.ForBaseType(typeof(OtherHolder));

        Assert.NotEqual(first[0].ScribeLabel, second[0].ScribeLabel);
    }

    [Fact]
    public void Add_KeyThatIsNotAnXmlName_Throws() {
        PropertyRegistry registry = new PropertyRegistry();

        Assert.Throws<ArgumentException>(() => registry.Add(typeof(Holder), "My Mod.count", typeof(int), null));
    }

    [Fact]
    public void Add_TypeScribeValuesCannotWrite_Throws() {
        Type[] unsupported = { typeof(List<int>), typeof(ThingDef), typeof(Thing) };

        foreach (Type valueType in unsupported) {
            PropertyRegistry registry = new PropertyRegistry();

            Assert.Throws<ArgumentException>(() => registry.Add(typeof(Holder), "Probe.x", valueType, null));
        }
    }
}
