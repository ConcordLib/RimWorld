using System;
using System.Reflection;

namespace Concord.RimWorld;

public static class MethodIdentity
{
    public static MethodBase Normalize(MethodBase target)
    {
        if (target.DeclaringType == null || target.DeclaringType.IsGenericTypeDefinition || target.IsGenericMethodDefinition)
        {
            return target;
        }

        try
        {
            return MethodBase.GetMethodFromHandle(target.MethodHandle, target.DeclaringType.TypeHandle) ?? target;
        }
        catch (NotSupportedException)
        {
            return target;
        }
    }
}
