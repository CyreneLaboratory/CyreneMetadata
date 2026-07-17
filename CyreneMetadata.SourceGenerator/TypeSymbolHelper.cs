using System.Linq;
using Microsoft.CodeAnalysis;

namespace CyreneMetadata.SourceGenerator;

internal static class TypeSymbolHelper
{
    public static string GetFullyQualifiedName(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    public static string GetTypeOfName(INamedTypeSymbol type)
    {
        if (type.IsGenericType && SymbolEqualityComparer.Default.Equals(type, type.OriginalDefinition))
            return type.ConstructUnboundGenericType().ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return GetFullyQualifiedName(type);
    }

    public static bool IsDerivedFrom(INamedTypeSymbol candidate, INamedTypeSymbol root)
    {
        if (SymbolEqualityComparer.Default.Equals(candidate, root)) return false;
        if (root.TypeKind == TypeKind.Interface) return candidate.AllInterfaces.Any(i => MatchesRoot(i, root));

        var current = candidate.BaseType;
        while (current is not null)
        {
            if (MatchesRoot(current, root)) return true;
            current = current.BaseType;
        }

        return false;
    }

    private static bool MatchesRoot(INamedTypeSymbol candidate, INamedTypeSymbol root)
    {
        if (root.IsUnboundGenericType)
            return SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, root.OriginalDefinition);

        return SymbolEqualityComparer.Default.Equals(candidate, root);
    }
}
