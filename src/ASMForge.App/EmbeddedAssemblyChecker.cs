using ASMForge.Core.Assembly;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ASMForge.App;

/// <summary>
/// Finds MIPS assembly embedded in C# source, i.e. string literals passed to <c>LoadAssembly(...)</c> directly or
/// through a const/variable, checks it like an .asm file, and maps each problem back to its line in the C# file.
/// </summary>
internal static class EmbeddedAssemblyChecker
{
    public static List<EditorDiagnostic> Check(string csharpSource, SimpleAssembler checker, Func<string, List<string>> warnings)
    {
        var diagnostics = new List<EditorDiagnostic>();
        var tree = CSharpSyntaxTree.ParseText(csharpSource);
        foreach (var literal in FindAssemblyLiterals(tree.GetRoot()))
        {
            var token = literal.Token;
            var startLine = tree.GetLineSpan(token.Span).StartLinePosition.Line;  // 0-based line of the opening quote
            var endLine = tree.GetLineSpan(token.Span).EndLinePosition.Line;
            // Where line 1 of the string's value sits (0-based): raw """ strings start on the next line,
            // verbatim @"..." strings on the same line; other strings can't be mapped line by line.
            int? firstValueLine = token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken) ? startLine + 1
                : token.Text.StartsWith('@') ? startLine
                : null;
            int MapLine(int line) => firstValueLine is { } first && line > 0
                ? Math.Clamp(first + line, startLine + 1, endLine + 1)
                : startLine + 1;

            var value = token.ValueText;
            foreach (var error in checker.CheckAll(value))
                diagnostics.Add(new EditorDiagnostic(MapLine(error.Line), null, $"Assembly: {error.Message}", EditorDiagnosticSeverity.Error));
            foreach (var warning in warnings(value))
                if (DiagnosticRenderer.FromAssemblyMessage(warning, EditorDiagnosticSeverity.Warning) is { } w)
                    diagnostics.Add(w with { Line = MapLine(w.Line), Message = $"Assembly: {w.Message}" });
        }
        return diagnostics;
    }

    // String literals used as LoadAssembly's first argument, directly or via an identifier's initializer.
    private static IEnumerable<LiteralExpressionSyntax> FindAssemblyLiterals(SyntaxNode root)
    {
        var found = new HashSet<LiteralExpressionSyntax>();
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = call.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                _ => null
            };
            if (name != "LoadAssembly" || call.ArgumentList.Arguments.Count == 0) continue;

            switch (call.ArgumentList.Arguments[0].Expression)
            {
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                    found.Add(literal);
                    break;
                case IdentifierNameSyntax identifier:
                    foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                        if (declarator.Identifier.ValueText == identifier.Identifier.ValueText &&
                            declarator.Initializer?.Value is LiteralExpressionSyntax initializer &&
                            initializer.IsKind(SyntaxKind.StringLiteralExpression))
                            found.Add(initializer);
                    break;
            }
        }
        return found;
    }
}
