using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace CyreneMetadata.SourceGenerator;

internal sealed class TypeCollector(Compilation compilation)
{
    private readonly Compilation Compilation = compilation;

    public CollectionResult Collect(
        IReadOnlyDictionary<INamedTypeSymbol, Location> deriveRoot,
        IReadOnlyDictionary<INamedTypeSymbol, Location> propRoot)
    {
        var rootRet = ImmutableArray.CreateBuilder<RootTypeResult>();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var creatableTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var pair in deriveRoot.OrderBy(p => TypeSymbolHelper.GetFullyQualifiedName(p.Key), StringComparer.Ordinal))
        {
            var root = pair.Key;
            var derivedTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var candidate in CollectCandidates(root))
            {
                if (!TypeSymbolHelper.IsDerivedFrom(candidate, root)) continue;

                if (!Compilation.IsSymbolAccessibleWithin(candidate, Compilation.Assembly))
                {
                    diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.InaccessibleDerivedType, pair.Value,
                        TypeSymbolHelper.GetFullyQualifiedName(candidate), TypeSymbolHelper.GetFullyQualifiedName(root)));
                    continue;
                }

                derivedTypes.Add(candidate);
                if (CanCreateInstance(candidate)) creatableTypes.Add(candidate);
            }

            var ordered = derivedTypes.OrderBy(TypeSymbolHelper.GetFullyQualifiedName, StringComparer.Ordinal).ToImmutableArray();
            rootRet.Add(new RootTypeResult(root, ordered));
        }

        var propRet = ImmutableArray.CreateBuilder<PropertyRootResult>();
        foreach (var pair in propRoot.OrderBy(p => TypeSymbolHelper.GetFullyQualifiedName(p.Key), StringComparer.Ordinal))
            propRet.Add(CollectProperties(pair.Key, creatableTypes));

        var orderedTypes = creatableTypes.OrderBy(TypeSymbolHelper.GetFullyQualifiedName, StringComparer.Ordinal).ToImmutableArray();
        return new CollectionResult(rootRet.ToImmutable(), propRet.ToImmutable(), orderedTypes, diagnostics.ToImmutable());
    }

    private PropertyRootResult CollectProperties(INamedTypeSymbol root, HashSet<INamedTypeSymbol> creatable)
    {
        var properties = ImmutableArray.CreateBuilder<PropertyMetadata>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        for (var current = root; current != null; current = current.BaseType)
        {
            var isRoot = SymbolEqualityComparer.Default.Equals(current, root);
            foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.IsIndexer) continue;
                if (member.Parameters.Length != 0) continue;
                if (!isRoot && member.DeclaredAccessibility == Accessibility.Private) continue;
                if (!seenNames.Add(member.Name)) continue;

                var metadata = BuildPropertyMetadata(root, member);
                if (metadata == null) continue;

                properties.Add(metadata);
                if (member.Type is INamedTypeSymbol propType && Compilation.IsSymbolAccessibleWithin(
                    propType, Compilation.Assembly) && CanCreateInstance(propType))
                    creatable.Add(propType);
            }
        }

        var ordered = properties.OrderBy(p => p.Name, StringComparer.Ordinal).ToImmutableArray();
        return new PropertyRootResult(root, ordered);
    }

    private PropertyMetadata? BuildPropertyMetadata(INamedTypeSymbol root, IPropertySymbol prop)
    {
        if (ContainsTypeParameter(prop.Type)) return null;

        var isStatic = prop.IsStatic;
        var isPublic = prop.DeclaredAccessibility == Accessibility.Public;
        var getter = prop.GetMethod is { } get && Compilation.IsSymbolAccessibleWithin(get, Compilation.Assembly);
        var setter = prop.SetMethod is { } set && !set.IsInitOnly && Compilation.IsSymbolAccessibleWithin(set, Compilation.Assembly);

        var attributes = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var attributeData in prop.GetAttributes())
        {
            var attribute = attributeData.AttributeClass;
            if (attribute == null || attribute.TypeKind == TypeKind.Error) continue;
            if (!Compilation.IsSymbolAccessibleWithin(attribute, Compilation.Assembly)) continue;
            if (!seen.Add(attribute)) continue;

            attributes.Add(attribute);
        }

        attributes.Sort((left, right) => StringComparer.Ordinal.Compare(
            TypeSymbolHelper.GetFullyQualifiedName(left), TypeSymbolHelper.GetFullyQualifiedName(right)));

        var declaringType = prop.ContainingType;
        if (!Compilation.IsSymbolAccessibleWithin(declaringType, Compilation.Assembly)) declaringType = root;

        return new PropertyMetadata(prop.Name, prop.Type, declaringType, root,
            isStatic, isPublic, getter, setter, attributes.ToImmutable());
    }

    private bool CanCreateInstance(INamedTypeSymbol type)
    {
        if (type.IsAbstract || type.TypeKind == TypeKind.Interface) return false;
        if (type.TypeArguments.Any(ContainsTypeParameter)) return false;
        if (type.TypeKind == TypeKind.Struct) return true;

        return type.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Length == 0 &&
            constructor.DeclaredAccessibility == Accessibility.Public &&
            Compilation.IsSymbolAccessibleWithin(constructor, Compilation.Assembly));
    }

    private static bool ContainsTypeParameter(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.TypeParameter) return true;
        return type is INamedTypeSymbol named && named.TypeArguments.Any(ContainsTypeParameter);
    }

    private IEnumerable<INamedTypeSymbol> CollectCandidates(INamedTypeSymbol root)
    {
        var assemblies = CollectRelevantAssemblies(root);
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var assembly in assemblies)
        {
            foreach (var type in EnumerateTypes(assembly.GlobalNamespace))
            {
                if (type.IsImplicitlyDeclared) continue;
                if (type.TypeKind is not (TypeKind.Class or TypeKind.Interface or TypeKind.Struct)) continue;
                if (IsGeneratedCacheType(type)) continue;
                if (seen.Add(type)) yield return type;
            }
        }
    }

    private IEnumerable<IAssemblySymbol> CollectRelevantAssemblies(INamedTypeSymbol root)
    {
        var rootAssembly = root.ContainingAssembly;
        var assemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default)
        {
            Compilation.Assembly
        };

        var coreAssembly = Compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
        if (SymbolEqualityComparer.Default.Equals(rootAssembly, coreAssembly)) return assemblies;

        foreach (var reference in Compilation.SourceModule.ReferencedAssemblySymbols)
            if (SymbolEqualityComparer.Default.Equals(reference, rootAssembly) || ReferencesAssembly(reference, rootAssembly))
                assemblies.Add(reference);

        return assemblies;
    }

    private static bool IsGeneratedCacheType(INamedTypeSymbol type)
    {
        return type.Name == "CyreneMetadataCache" && type.ContainingNamespace.ToDisplayString() == "CyreneMetadata";
    }

    private static bool ReferencesAssembly(IAssemblySymbol assembly, IAssemblySymbol target)
    {
        var visited = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        var pending = new Queue<IAssemblySymbol>();
        pending.Enqueue(assembly);
        visited.Add(assembly);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            foreach (var module in current.Modules)
                foreach (var reference in module.ReferencedAssemblySymbols)
                {
                    if (SymbolEqualityComparer.Default.Equals(reference, target)) return true;
                    if (visited.Add(reference)) pending.Enqueue(reference);
                }
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in EnumerateNestedTypes(type)) yield return nested;
        }

        foreach (var child in namespaceSymbol.GetNamespaceMembers())
            foreach (var type in EnumerateTypes(child)) yield return type;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol containingType)
    {
        foreach (var nested in containingType.GetTypeMembers())
        {
            yield return nested;
            foreach (var descendant in EnumerateNestedTypes(nested)) yield return descendant;
        }
    }
}

internal sealed class CollectionResult(ImmutableArray<RootTypeResult> roots, ImmutableArray<PropertyRootResult> propertyRoots,
    ImmutableArray<INamedTypeSymbol> creatableTypes, ImmutableArray<Diagnostic> diagnostics)
{
    public ImmutableArray<RootTypeResult> Roots { get; } = roots;
    public ImmutableArray<PropertyRootResult> PropertyRoots { get; } = propertyRoots;
    public ImmutableArray<INamedTypeSymbol> CreatableTypes { get; } = creatableTypes;
    public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;
}

internal sealed class RootTypeResult(INamedTypeSymbol rootType, ImmutableArray<INamedTypeSymbol> derivedTypes)
{
    public INamedTypeSymbol RootType { get; } = rootType;
    public ImmutableArray<INamedTypeSymbol> DerivedTypes { get; } = derivedTypes;
}

internal sealed class PropertyRootResult(INamedTypeSymbol rootType, ImmutableArray<PropertyMetadata> properties)
{
    public INamedTypeSymbol RootType { get; } = rootType;
    public ImmutableArray<PropertyMetadata> Properties { get; } = properties;
}

internal sealed class PropertyMetadata(string name, ITypeSymbol propertyType, INamedTypeSymbol declaringType,
    INamedTypeSymbol ownerType, bool isStatic, bool isPublic, bool getter, bool setter, ImmutableArray<INamedTypeSymbol> attributes)
{
    public string Name { get; } = name;
    public ITypeSymbol PropertyType { get; } = propertyType;
    public INamedTypeSymbol DeclaringType { get; } = declaringType;
    public INamedTypeSymbol OwnerType { get; } = ownerType;

    public bool IsStatic { get; } = isStatic;
    public bool IsPublic { get; } = isPublic;
    public bool Getter { get; } = getter;
    public bool Setter { get; } = setter;
    public ImmutableArray<INamedTypeSymbol> Attributes { get; } = attributes;
}
