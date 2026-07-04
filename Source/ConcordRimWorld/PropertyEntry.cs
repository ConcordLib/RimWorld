namespace Concord.RimWorld;

public sealed record PropertyEntry(
    System.Type BaseType,
    string Key,
    System.Type ValueType,
    System.Func<object, bool> Validate,
    IAttachedSlot Slot,
    string ScribeLabel);
