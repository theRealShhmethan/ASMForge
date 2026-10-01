using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace ASMForge.App;

/// <summary>
/// Editor gutter that shows breakpoints as red dots. Clicking the gutter toggles a breakpoint on that line.
/// Breakpoints are stored as text anchors, so they follow their line while the document is edited
/// and disappear when the line is deleted.
/// </summary>
internal sealed class BreakpointMargin : AbstractMargin
{
    private const double MarginWidth = 16;
    private static readonly IBrush DotBrush = new ImmutableSolidColorBrush(Color.FromRgb(220, 50, 47));
    private readonly List<TextAnchor> _anchors = new();

    public BreakpointMargin()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public event EventHandler? BreakpointsChanged;

    /// <summary>1-based line numbers that currently have a breakpoint, in ascending order.</summary>
    public IReadOnlyList<int> Lines
    {
        get
        {
            _anchors.RemoveAll(a => a.IsDeleted);
            return _anchors.Select(a => a.Line).Distinct().OrderBy(line => line).ToList();
        }
    }

    public bool HasBreakpoint(int line) => _anchors.Any(a => !a.IsDeleted && a.Line == line);

    public void Toggle(int line)
    {
        var document = Document;
        if (document is null || line < 1 || line > document.LineCount) return;

        _anchors.RemoveAll(a => a.IsDeleted);
        if (_anchors.RemoveAll(a => a.Line == line) == 0)
            _anchors.Add(document.CreateAnchor(document.GetLineByNumber(line).Offset));

        InvalidateVisual();
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_anchors.Count == 0) return;
        _anchors.Clear();
        InvalidateVisual();
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= TextView_VisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += TextView_VisualLinesChanged;
        InvalidateVisual();
    }

    private void TextView_VisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        // A transparent fill makes the whole gutter clickable, not just the dots.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        var textView = TextView;
        if (textView is not { VisualLinesValid: true }) return;

        foreach (var visualLine in textView.VisualLines)
        {
            if (!HasBreakpoint(visualLine.FirstDocumentLine.LineNumber)) continue;
            var textLine = visualLine.TextLines[0];
            var top = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.TextTop) - textView.VerticalOffset;
            var bottom = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.TextBottom) - textView.VerticalOffset;
            var radius = Math.Max(2, Math.Min(MarginWidth, bottom - top) / 2 - 2);
            context.DrawEllipse(DotBrush, null, new Point(MarginWidth / 2, (top + bottom) / 2), radius, radius);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var textView = TextView;
        if (textView is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        textView.EnsureVisualLines();
        var visualLine = textView.GetVisualLineFromVisualTop(e.GetPosition(textView).Y + textView.VerticalOffset);
        if (visualLine is null) return;

        Toggle(visualLine.FirstDocumentLine.LineNumber);
        e.Handled = true;
    }
}
