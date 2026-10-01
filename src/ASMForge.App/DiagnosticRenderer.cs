using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using System.Text.RegularExpressions;

namespace ASMForge.App;

internal enum EditorDiagnosticSeverity { Error, Warning }

/// <summary>An error or warning attached to a 1-based source line (and optional 1-based column).</summary>
internal sealed record EditorDiagnostic(int Line, int? Column, string Message, EditorDiagnosticSeverity Severity);

/// <summary>
/// Draws red (error) and yellow (warning) squiggles under source lines that have diagnostics.
/// </summary>
internal sealed class DiagnosticRenderer : IBackgroundRenderer
{
    private static readonly IPen ErrorPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(230, 60, 60)), 1.2);
    private static readonly IPen WarningPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(220, 170, 0)), 1.2);
    private static readonly Regex AssemblyLine = new(@"^Line (\d+):\s*", RegexOptions.Compiled);
    private static readonly Regex CSharpLine = new(@"^(?<file>[^()\r\n]+)\((?<line>\d+),(?<col>\d+)\): (?<severity>error|warning) (?<message>.*)$", RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly TextView _textView;
    private readonly List<EditorDiagnostic> _diagnostics = new();

    public DiagnosticRenderer(TextView textView) => _textView = textView;

    public KnownLayer Layer => KnownLayer.Selection;

    public IReadOnlyList<EditorDiagnostic> Diagnostics => _diagnostics;

    public void Set(IEnumerable<EditorDiagnostic> diagnostics)
    {
        _diagnostics.Clear();
        _diagnostics.AddRange(diagnostics);
        _textView.InvalidateLayer(Layer);
    }

    public void Clear()
    {
        if (_diagnostics.Count == 0) return;
        _diagnostics.Clear();
        _textView.InvalidateLayer(Layer);
    }

    /// <summary>The most severe diagnostic on a line, or null.</summary>
    public EditorDiagnostic? At(int line) =>
        _diagnostics.Where(d => d.Line == line).OrderBy(d => d.Severity).FirstOrDefault();

    /// <summary>Parses an assembler/runtime message of the form "Line N: ..." into an error diagnostic.</summary>
    public static EditorDiagnostic? FromAssemblyMessage(string message, EditorDiagnosticSeverity severity = EditorDiagnosticSeverity.Error)
    {
        var match = AssemblyLine.Match(message);
        return match.Success && int.TryParse(match.Groups[1].Value, out var line)
            ? new EditorDiagnostic(line, null, message[match.Length..].Trim(), severity)
            : null;
    }

    /// <summary>Parses Roslyn diagnostics formatted as "File.cs(line,col): error CS0001: message".</summary>
    public static IEnumerable<(string File, EditorDiagnostic Diagnostic)> FromCSharpDiagnostics(string text)
    {
        foreach (Match match in CSharpLine.Matches(text))
        {
            var severity = match.Groups["severity"].Value == "error" ? EditorDiagnosticSeverity.Error : EditorDiagnosticSeverity.Warning;
            yield return (match.Groups["file"].Value.Trim(),
                new EditorDiagnostic(int.Parse(match.Groups["line"].Value), int.Parse(match.Groups["col"].Value),
                    match.Groups["message"].Value.Trim(), severity));
        }
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var document = textView.Document;
        if (_diagnostics.Count == 0 || document is null || !textView.VisualLinesValid) return;

        foreach (var diagnostic in _diagnostics)
        {
            if (diagnostic.Line < 1 || diagnostic.Line > document.LineCount) continue;
            var line = document.GetLineByNumber(diagnostic.Line);
            var text = document.GetText(line);
            var trimmedStart = text.Length - text.TrimStart().Length;
            var trimmedEnd = text.TrimEnd().Length;
            if (trimmedEnd <= trimmedStart) continue; // blank line: nothing to underline

            var segment = new TextSegment { StartOffset = line.Offset + trimmedStart, EndOffset = line.Offset + trimmedEnd };
            var pen = diagnostic.Severity == EditorDiagnosticSeverity.Error ? ErrorPen : WarningPen;
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                DrawSquiggle(drawingContext, rect, pen);
        }
    }

    private static void DrawSquiggle(DrawingContext context, Rect rect, IPen pen)
    {
        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            var y = rect.Bottom - 1;
            var x = rect.Left;
            var up = true;
            figure.BeginFigure(new Point(x, y), false);
            while (x < rect.Right)
            {
                x = Math.Min(x + 2, rect.Right);
                figure.LineTo(new Point(x, up ? y - 2 : y));
                up = !up;
            }
            figure.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }
}
