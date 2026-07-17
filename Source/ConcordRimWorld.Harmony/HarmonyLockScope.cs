using System;
using System.Reflection;
using System.Threading;

namespace Concord.RimWorld.Harmony;

internal sealed class HarmonyLockScope : IDisposable
{
    private static readonly object Locker = ResolveLocker();

    internal static bool Available { get; } = Locker != null;

    private HarmonyLockScope()
    {
    }

    internal static HarmonyLockScope Enter()
    {
        Monitor.Enter(Locker);
        return new HarmonyLockScope();
    }

    public void Dispose()
    {
        Monitor.Exit(Locker);
    }

    private static object ResolveLocker()
    {
        FieldInfo field = typeof(HarmonyLib.PatchProcessor).GetField("locker", BindingFlags.NonPublic | BindingFlags.Static);
        if (field == null)
        {
            return null;
        }

        object value = field.GetValue(null);
        return value;
    }
}
