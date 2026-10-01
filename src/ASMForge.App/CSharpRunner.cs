using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace ASMForge.App;

internal sealed record CSharpSource(string Path, string Text);
internal sealed record CSharpRunResult(bool Success, string Output, string Diagnostics);

internal static class CSharpRunner
{
    public static async Task<CSharpRunResult> CompileAndRunAsync(IReadOnlyList<CSharpSource> sources)
    {
        if (sources.Count == 0)
            return new CSharpRunResult(false, string.Empty, "No C# source files were found to compile.");

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var syntaxTrees = sources
            .Select(s => CSharpSyntaxTree.ParseText(SourceText.From(s.Text, Encoding.UTF8), parseOptions, s.Path))
            .ToArray();

        var references = GetMetadataReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName: $"ASMForge.UserProgram.{Guid.NewGuid():N}",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.ConsoleApplication,
                optimizationLevel: OptimizationLevel.Debug,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: false));

        await using var peStream = new MemoryStream();
        await using var pdbStream = new MemoryStream();
        var emit = compilation.Emit(peStream, pdbStream);
        if (!emit.Success)
        {
            var diagnostics = string.Join(Environment.NewLine,
                emit.Diagnostics
                    .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
                    .OrderBy(d => d.Location.GetLineSpan().Path)
                    .ThenBy(d => d.Location.GetLineSpan().StartLinePosition.Line)
                    .Select(FormatDiagnostic));
            return new CSharpRunResult(false, string.Empty, diagnostics);
        }

        peStream.Position = 0;
        pdbStream.Position = 0;

        try
        {
            // Load into the default context so the dynamically compiled program resolves
            // the already-loaded ASMForge.Core assembly without a second copy.
            var assembly = Assembly.Load(peStream.ToArray(), pdbStream.ToArray());
            var entryPoint = assembly.EntryPoint;
            if (entryPoint is null)
                return new CSharpRunResult(false, string.Empty, "Compilation succeeded, but no C# entry point (Main) was found.");

            var oldOut = Console.Out;
            var oldError = Console.Error;
            using var writer = new StringWriter();
            try
            {
                Console.SetOut(writer);
                Console.SetError(writer);

                object?[]? args = entryPoint.GetParameters().Length switch
                {
                    0 => null,
                    1 => new object?[] { Array.Empty<string>() },
                    _ => throw new InvalidOperationException("Unsupported C# Main signature.")
                };

                var returnValue = entryPoint.Invoke(null, args);
                if (returnValue is Task task)
                    await task.ConfigureAwait(false);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                return new CSharpRunResult(false, writer.ToString(), ex.InnerException.ToString());
            }
            finally
            {
                Console.SetOut(oldOut);
                Console.SetError(oldError);
            }

            return new CSharpRunResult(true, writer.ToString(), "C# compilation completed successfully.");
        }
        catch (Exception ex)
        {
            return new CSharpRunResult(false, string.Empty, ex.ToString());
        }
    }

    private static IReadOnlyList<MetadataReference> GetMetadataReferences()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                paths.Add(path);
        }

        // The user program can call ASMForgeRuntime directly.
        paths.Add(typeof(ASMForge.Core.Execution.ASMForgeRuntime).Assembly.Location);
        paths.Add(typeof(CSharpRunner).Assembly.Location);

        return paths
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static string FormatDiagnostic(Diagnostic diagnostic)
    {
        if (!diagnostic.Location.IsInSource)
            return diagnostic.ToString();

        var span = diagnostic.Location.GetLineSpan();
        var file = string.IsNullOrWhiteSpace(span.Path) ? "<source>" : Path.GetFileName(span.Path);
        var line = span.StartLinePosition.Line + 1;
        var column = span.StartLinePosition.Character + 1;
        return $"{file}({line},{column}): {diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Id}: {diagnostic.GetMessage()}";
    }
}
