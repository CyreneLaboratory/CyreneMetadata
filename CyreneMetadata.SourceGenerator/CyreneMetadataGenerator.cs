using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CyreneMetadata.SourceGenerator;

[Generator]
public sealed class CyreneMetadataGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var invocations = context.SyntaxProvider.CreateSyntaxProvider(static (n, _) =>
            InvocationCollector.IsCandidate(n), static (s, c) => InvocationCollector.Collect(s, c)).Where(static r =>
                r != null).Select(static (r, _) => r!);

        context.RegisterSourceOutput(invocations.Collect().Combine(context.CompilationProvider), static (output, input) =>
        {
            var deriveRoots = new Dictionary<INamedTypeSymbol, Location>(SymbolEqualityComparer.Default);
            var propertyRoots = new Dictionary<INamedTypeSymbol, Location>(SymbolEqualityComparer.Default);
            var signatures = new SortedDictionary<string, ImmutableArray<ITypeSymbol?>>(StringComparer.Ordinal);
            foreach (var invocation in input.Left)
            {
                if (invocation.Diagnostic != null)
                {
                    output.ReportDiagnostic(invocation.Diagnostic);
                    continue;
                }
                if (invocation.Signature is { } signature)
                {
                    var key = string.Join(", ", signature.Select(t => t == null ? "?" : TypeSymbolHelper.GetFullyQualifiedName(t)));
                    signatures[key] = signature;
                    continue;
                }
                if (invocation.RootType == null || invocation.Location == null) continue;

                var target = invocation.Facet == MetadataFacet.Properties ? propertyRoots : deriveRoots;
                if (!target.ContainsKey(invocation.RootType)) target.Add(invocation.RootType, invocation.Location);
            }

            var result = new TypeCollector(input.Right).Collect(deriveRoots, propertyRoots, signatures.Values);
            foreach (var diagnostic in result.Diagnostics) output.ReportDiagnostic(diagnostic);

            output.AddSource("CyreneMetadataCache.Dispatcher.g.cs", SourceText.From(CacheEmitter.Emit(result), Encoding.UTF8));
        });
    }
}
