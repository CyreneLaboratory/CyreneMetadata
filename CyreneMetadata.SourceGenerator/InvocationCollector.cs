using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CyreneMetadata.SourceGenerator;

internal enum MetadataFacet
{
    Derive,
    Properties,
    Constructor
}

internal static class InvocationCollector
{
    private const string CacheNamespace = "CyreneMetadata";
    private const string CacheTypeName = "CyreneMetadataCache";
    private const string DeriveMethod = "GetDerive";
    private const string PropertiesMethod = "GetProperties";
    private const string CreateMethod = "CreateInstance";

    public static bool IsCandidate(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation) return false;

        var invoke = invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax { Name: IdentifierNameSyntax identifier } => identifier.Identifier.ValueText,
            _ => null
        };
        return invoke is DeriveMethod or PropertiesMethod or CreateMethod;
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
            CreateMethod => MetadataFacet.Constructor,
            _ => null
        };
    }

    public static InvocationResult? Collect(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var symbol = context.SemanticModel.GetSymbolInfo(invocation, cancellationToken);
        var method = symbol.Symbol as IMethodSymbol ?? symbol.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (method == null || GetCacheFacet(method) is not { } facet) return null;
        if (facet == MetadataFacet.Constructor) return CollectCtor(context, invocation, method, cancellationToken);

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

    private static InvocationResult? CollectCtor(GeneratorSyntaxContext context,
        InvocationExpressionSyntax invocation, IMethodSymbol method, CancellationToken cancellationToken)
    {
        if (method.Parameters.Length != 2 || !method.Parameters[1].IsParams) return null;

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count < 2) return null;

        // Arguments are listed inline, or wrapped in a collection / array literal
        IEnumerable<ExpressionSyntax>? elements = arguments.Skip(1).Select(a => a.Expression);
        if (arguments.Count == 2)
        {
            var single = arguments[1].Expression;
            if (single is CollectionExpressionSyntax collection)
                elements = collection.Elements.All(e => e is ExpressionElementSyntax)
                    ? collection.Elements.Select(e => ((ExpressionElementSyntax)e).Expression) : null;
            else if (single is ArrayCreationExpressionSyntax { Initializer: { } init })
                elements = init.Expressions;
            else if (single is ImplicitArrayCreationExpressionSyntax implicitArray)
                elements = implicitArray.Initializer.Expressions;
            else if (context.SemanticModel.GetTypeInfo(single, cancellationToken).Type is IArrayTypeSymbol)
                elements = null;
        }

        if (elements == null)
            return InvocationResult.ForDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.RuntimeCtorArguments, invocation.GetLocation()));

        var signature = ImmutableArray.CreateBuilder<ITypeSymbol?>();
        foreach (var element in elements)
        {
            var type = context.SemanticModel.GetTypeInfo(element, cancellationToken).Type;
            if (type?.TypeKind == TypeKind.Error) return null;

            // Unknown static type (null literal, type parameter) matches any nullable parameter
            signature.Add(type?.TypeKind == TypeKind.TypeParameter ? null : type);
        }

        if (signature.Count == 0) return null;
        return InvocationResult.ForSignature(signature.ToImmutable(), invocation.GetLocation());
    }
}

internal sealed class InvocationResult
{
    public MetadataFacet Facet { get; set; }
    public Location? Location { get; set; }
    public INamedTypeSymbol? RootType { get; set; }
    public Diagnostic? Diagnostic { get; set; }
    public ImmutableArray<ITypeSymbol?>? Signature { get; set; }

    public static InvocationResult ForRoot(INamedTypeSymbol rootType, Location location, MetadataFacet facet)
    {
        return new InvocationResult
        {
            RootType = rootType,
            Location = location, 
            Facet = facet
        };
    }

    public static InvocationResult ForSignature(ImmutableArray<ITypeSymbol?> signature, Location location)
    {
        return new InvocationResult
        {
            Signature = signature,
            Location = location,
            Facet = MetadataFacet.Constructor
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
