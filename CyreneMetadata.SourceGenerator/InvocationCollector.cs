using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CyreneMetadata.SourceGenerator;

internal enum MetadataFacet
{
    Derive,
    Properties
}

internal static class InvocationCollector
{
    private const string CacheNamespace = "CyreneMetadata";
    private const string CacheTypeName = "CyreneMetadataCache";
    private const string DeriveMethod = "GetDerive";
    private const string PropertiesMethod = "GetProperties";

    public static bool IsCandidate(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation) return false;

        var invoke = invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax { Name: IdentifierNameSyntax identifier } => identifier.Identifier.ValueText,
            _ => null
        };
        return invoke is DeriveMethod or PropertiesMethod;
    }

    private static MetadataFacet? GetCacheFacet(IMethodSymbol? method)
    {
        if (method == null) return null;

        var type = method.ContainingType;
        if (type.Name != CacheTypeName || type.ContainingNamespace.ToDisplayString() != CacheNamespace) return null;

        return method.Name switch
        {
            DeriveMethod => MetadataFacet.Derive,
            PropertiesMethod => MetadataFacet.Properties,
            _ => null
        };
    }

    public static InvocationResult? Collect(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var symbol = context.SemanticModel.GetSymbolInfo(invocation, cancellationToken);
        var method = symbol.Symbol as IMethodSymbol ?? symbol.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (method == null || GetCacheFacet(method) is not { } facet) return null;

        var arg = invocation.ArgumentList.Arguments.Count >= 1 ? invocation.ArgumentList.Arguments[0].Expression : null;
        if (arg is not TypeOfExpressionSyntax typeOf)
            return InvocationResult.ForDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RuntimeTypeArgument, invocation.GetLocation()));

        var typeInfo = context.SemanticModel.GetTypeInfo(typeOf.Type, cancellationToken);
        if (typeInfo.Type is INamedTypeSymbol root && root.TypeKind != TypeKind.Error)
            return InvocationResult.ForRoot(root, invocation.GetLocation(), facet);

        return InvocationResult.ForDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.InvalidTypeOfArgument, typeOf.GetLocation()));
    }
}

internal sealed class InvocationResult
{
    public MetadataFacet Facet { get; set; }
    public Location? Location { get; set; }
    public INamedTypeSymbol? RootType { get; set; }
    public Diagnostic? Diagnostic { get; set; }

    public static InvocationResult ForRoot(INamedTypeSymbol rootType, Location location, MetadataFacet facet)
    {
        return new InvocationResult
        {
            RootType = rootType,
            Location = location, 
            Facet = facet
        };
    }

    public static InvocationResult ForDiagnostic(Diagnostic diagnostic)
    {
        return new InvocationResult
        {
            Location = diagnostic.Location,
            Diagnostic = diagnostic
        };
    }
}
