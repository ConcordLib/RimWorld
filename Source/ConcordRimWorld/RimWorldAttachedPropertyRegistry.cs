using System;
using Concord.AttachedData;
using Concord.Orchestration;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Concord.RimWorld;

public sealed class RimWorldAttachedPropertyRegistry : IAttachedPropertyRegistry {
    private readonly PropertyRegistry registry;

    public RimWorldAttachedPropertyRegistry(PropertyRegistry registry) {
        this.registry = registry;
    }

    public void RegisterAttachedProperty(Type declarationType, Type baseType, string name, Type valueType, IAttachedSlot slot) {
        string key = declarationType.Assembly.GetName().Name + "." + name;
        if (!IsPersisted(baseType)) {
            Log.Warning("[Concord.RimWorld] Attached field '" + declarationType.FullName + "." + name + "' targets " + baseType.Name + ", which Concord does not scribe. It will work, but it will not be saved. Concord hooks Thing, Faction, WorldObject, Hediff, ThingComp, MapComponent, WorldComponent and GameComponent.");
        }

        try {
            registry.Add(baseType, key, valueType, null, slot);
        } catch (Exception e) {
            Log.Error("[Concord.RimWorld] Attached field '" + declarationType.FullName + "." + name + "' will not be saved: " + e.Message);
        }
    }

    private static readonly Type[] ScribedRoots = {
        typeof(Thing), typeof(Faction), typeof(WorldObject), typeof(Hediff),
        typeof(ThingComp), typeof(MapComponent), typeof(WorldComponent), typeof(GameComponent),
    };

    private static bool IsPersisted(Type baseType) {
        foreach (Type root in ScribedRoots) {
            if (root.IsAssignableFrom(baseType)) {
                return true;
            }
        }

        return false;
    }
}
