using Microsoft.CodeAnalysis;

namespace CyreneMetadata.SourceGenerator;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor RuntimeTypeArgument = new("CYMT001",
        "The metadata root must be known", "", "CyreneMetadata", DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor InaccessibleDerivedType = new("CYMT002",
        "A derived type is inaccessible to generated code", "", "CyreneMetadata", DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor InvalidTypeOfArgument = new("CYMT003",
        "The metadata root is invalid", "", "CyreneMetadata", DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor RuntimeCtorArguments = new("CYMT004",
        "The constructor arguments must be listed inline", "", "CyreneMetadata", DiagnosticSeverity.Error, true);
}
