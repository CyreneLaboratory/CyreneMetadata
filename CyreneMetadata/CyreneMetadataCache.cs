using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CyreneMetadata;

public static class CyreneMetadataCache
{
    private const BindingFlags DefaultFlag = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

    private static readonly ConcurrentDictionary<Type, Type[]> Derives = new();

    public static Type[] GetDerive(Type baseType)
    {
        if (!Derives.TryGetValue(baseType, out var types)) return [];
        return types;
    }

    private static readonly ConcurrentDictionary<Type, Func<object>> Ctors = new();

    public static object CreateInstance(Type concreteType)
    {
        if (Ctors.TryGetValue(concreteType, out var factory)) return factory();
        throw new ArgumentException($"Type '{concreteType}' not have default constructor.");
    }

    private static readonly ConcurrentDictionary<Type, ArgCtor[]> ArgCtors = new();

    public static object CreateInstance(Type concreteType, params object?[] args)
    {
        args ??= [null];
        if (args.Length == 0) return CreateInstance(concreteType);

        if (ArgCtors.TryGetValue(concreteType, out var ctors))
            foreach (var ctor in ctors)
                if (ctor.Match(args)) return ctor.Factory(args);

        var signature = string.Join(", ", args.Select(arg => arg?.GetType().Name ?? "null"));
        throw new ArgumentException($"Type '{concreteType}' not have constructor ({signature}).");
    }

    private static readonly ConcurrentDictionary<Type, CyrenePropertyInfo[]> Properties = new();
    public static CyrenePropertyInfo[] GetProperties(Type type) => GetProperties(type, DefaultFlag);
    public static CyrenePropertyInfo[] GetProperties(Type type, BindingFlags flags)
    {
        if (!Properties.TryGetValue(type, out var all) || all.Length == 0) return [];

        var wantPublic = (flags & BindingFlags.Public) != 0;
        var wantNonPublic = (flags & BindingFlags.NonPublic) != 0;
        var wantInstance = (flags & BindingFlags.Instance) != 0;
        var wantStatic = (flags & BindingFlags.Static) != 0;
        var declaredOnly = (flags & BindingFlags.DeclaredOnly) != 0;
        var flattenHierarchy = (flags & BindingFlags.FlattenHierarchy) != 0;

        var result = new List<CyrenePropertyInfo>(all.Length);
        foreach (var property in all)
        {
            if (property.IsStatic ? !wantStatic : !wantInstance) continue;
            if (property.IsPublic ? !wantPublic : !wantNonPublic) continue;
            if (property.DeclaringType != type && (declaredOnly || (property.IsStatic && !flattenHierarchy))) continue;

            result.Add(property);
        }

        return [.. result];
    }

    public static void RegisterDerive(Type baseType, Type[] derivedTypes)
    {
        Derives.AddOrUpdate(baseType, derivedTypes, (_, existing) => Union(existing, derivedTypes));
    }

    public static void RegisterCtor(Type concreteType, Func<object> ctor)
    {
        Ctors.TryAdd(concreteType, ctor);
    }

    public static void RegisterCtor(Type concreteType, Type[] parameterTypes, Func<object?[], object> ctor)
    {
        var added = new ArgCtor(parameterTypes, ctor);
        ArgCtors.AddOrUpdate(concreteType, [added], (_, existing) =>
            existing.Any(e => e.Parameters.SequenceEqual(parameterTypes)) ? existing : [.. existing, added]);
    }

    public static void RegisterProp(Type type, CyrenePropertyInfo[] prop)
    {
        Properties.TryAdd(type, prop);
    }

    private static Type[] Union(Type[] first, Type[] second)
    {
        var set = new HashSet<Type>(first);
        var added = false;
        foreach (var type in second)
            added |= set.Add(type);

        if (!added) return first;

        var merged = new Type[set.Count];
        set.CopyTo(merged);
        return merged;
    }
}

internal sealed class ArgCtor(Type[] parameters, Func<object?[], object> factory)
{
    public Type[] Parameters { get; } = parameters;
    public Func<object?[], object> Factory { get; } = factory;

    public bool Match(object?[] args)
    {
        if (args.Length != Parameters.Length) return false;

        for (var i = 0; i < args.Length; i++)
        {
            var parameter = Parameters[i];
            var arg = args[i];
            if (arg == null)
            {
                if (parameter.IsValueType && Nullable.GetUnderlyingType(parameter) == null) return false;
                continue;
            }

            if (!parameter.IsInstanceOfType(arg)) return false;
        }

        return true;
    }
}
