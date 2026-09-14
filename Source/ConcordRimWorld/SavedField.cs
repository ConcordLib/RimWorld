using System;
using Concord.AttachedData;

namespace Concord.RimWorld;

public sealed class SavedField<TTarget, TValue> : IAttachedSlot
    where TTarget : class {
    private readonly AttachedField<TTarget, TValue> field = new AttachedField<TTarget, TValue>();

    private SavedField() {
    }

    public static SavedField<TTarget, TValue> Register(string key) {
        SavedField<TTarget, TValue> saved = new SavedField<TTarget, TValue>();
        PropertyRegistry registry = RimWorldRuntime.Registry;
        if (registry == null) {
            throw new InvalidOperationException("Concord.RimWorld is not wired yet. Register saved fields from your Mod constructor or later.");
        }

        registry.Add(typeof(TTarget), key, typeof(TValue), null, saved);
        return saved;
    }

    public TValue Get(TTarget target) {
        return field.Get(target);
    }

    public void Set(TTarget target, TValue value) {
        field.Set(target, value);
    }

    public bool TryGet(TTarget target, out TValue value) {
        return field.TryGet(target, out value);
    }

    public ref TValue GetOrAddRef(TTarget target) {
        return ref field.GetOrAddRef(target);
    }

    object IAttachedSlot.Get(object target) {
        return field.Get((TTarget)target);
    }

    void IAttachedSlot.Set(object target, object value) {
        field.Set((TTarget)target, value is TValue typed ? typed : default);
    }
}
