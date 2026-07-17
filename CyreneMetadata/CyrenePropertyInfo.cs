using System;

namespace CyreneMetadata;

public sealed class CyrenePropertyInfo(string name, Type propertyType, Type declaringType,
    bool isStatic, bool isPublic, Func<object?, object?>? get, Action<object?, object?>? set, Type[] attributes)
{
    public string Name { get; } = name;
    public Type PropertyType { get; } = propertyType;
    public Type DeclaringType { get; } = declaringType;
    public bool IsStatic { get; } = isStatic;
    public bool IsPublic { get; } = isPublic;
    public Func<object?, object?>? Get { get; } = get;
    public Action<object?, object?>? Set { get; } = set;
    public bool CanRead => Get != null;
    public bool CanWrite => Set != null;
    public Type[] Attributes { get; } = attributes;
    public bool HasAttribute(Type attributeType) => Array.IndexOf(Attributes, attributeType) >= 0;
}
