using System;
using Concord.Orchestration;

namespace Concord.RimWorld;

public sealed class RimWorldAttachedPropertyRegistry : IAttachedPropertyRegistry {
    private readonly string modId;
    private readonly PropertyRegistry registry;

    public RimWorldAttachedPropertyRegistry(PropertyRegistry registry, string modId) {
        this.registry = registry;
        this.modId = modId;
    }

    public void RegisterAttachedProperty(Type baseType, string name, Type valueType) {
        string key = modId + "." + name;
        registry.Add(baseType, key, valueType, null);
    }
}
