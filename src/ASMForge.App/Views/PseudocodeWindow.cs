using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using ASMForge.Core.Pseudocode;

namespace ASMForge.App.Views;

/// <summary>
/// Tools > Pseudocode Generator: write pseudocode on the left and see the generated MIPS on the right, updated
/// live, with errors squiggled. The result can be opened as a new tab, inserted into the current file or copied.
/// </summary>
internal sealed class PseudocodeWindow : Window
{
    public const string Example = """
        // FizzBuzz: print 1 to 100, but Fizz for multiples of 3, Buzz for 5, FizzBuzz for both.
        int i = 1;

        while (i <= 100) {
            if (i % 3 == 0 && i % 5 == 0) print("FizzBuzz\n");
            else if (i % 3 == 0) print("Fizz\n");
            else if (i % 5 == 0) print("Buzz\n");
            else print(i, "\n");
            i++;
        }
        """;

    public const string CheatSheet = """
        Variables      int x = 5;   int a[10];   int b[3] = {1, 2, 3};
        Assignment     x = x + 1;   x += 2;   (also -= *= /= %=)   x++;   x--;   a[i] = 7;
        Decisions      if (x > 0 && y != 0) { ... } else if (x == 0) { ... } else { ... }
        Loops          while (x < 10) { ... }    do { ... } while (x < 10);
                       for (int i = 0; i < 10; i++) { ... }    break;    continue;
        Operators      + - * / %    == != < <= > >=    && || !    ( )    (C precedence)
        Output         print(x);   print("text\n");   print("x = ", x, "\n");   print('c');   printChar(x);
        Input          int n = readInt();   int c = readChar();
        Other          exit();    // comment    /* comment */    lines may also start with #
        """;

    private readonly TextEditor _input;
    private readonly TextEditor _output;
    private readonly DiagnosticRenderer _diagnostics;
    private readonly TextBlock _status;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Action<string> _openAsTab;
    private readonly Action<string> _insertIntoCurrentFile;
    private string _assembly = string.Empty;

    public PseudocodeWindow(string initialText, Action<string> openAsTab, Action<string> insertIntoCurrentFile)
    {
        _openAsTab = openAsTab;
        _insertIntoCurrentFile = insertIntoCurrentFile;
        Title = "Pseudocode Generator";
        Width = 1200;
        Height = 760;
        MinWidth = 760;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _input = CreateEditor(readOnly: false, "input.pseudo");
        _input.Text = initialText;
        _output = CreateEditor(readOnly: true, "output.asm");
        _diagnostics = new DiagnosticRenderer(_input.TextArea.TextView);
        _input.TextArea.TextView.BackgroundRenderers.Add(_diagnostics);
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono,Consolas") };

        var example = new Button { Content = "Load Example" };
        example.Click += (_, _) => _input.Text = Example;
        var openTab = new Button { Content = "Open as New Tab" };
        openTab.Click += (_, _) => { if (_assembly.Length > 0) _openAsTab(_assembly); };
        var insert = new Button { Content = "Insert into Current File" };
        insert.Click += (_, _) => { if (_assembly.Length > 0) _insertIntoCurrentFile(_assembly); };
        var copy = new Button { Content = "Copy Assembly" };
        copy.Click += async (_, _) =>
        {
            if (_assembly.Length > 0 && Clipboard is not null) await Clipboard.SetTextAsync(_assembly);
            _status.Text = "Assembly copied to the clipboard.";
        };

        var editors = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,*") };
        editors.Children.Add(Titled("Pseudocode", _input, 0));
        editors.Children.Add(new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent, [Grid.ColumnProperty] = 1 });
        editors.Children.Add(Titled("Generated MIPS assembly", _output, 2));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { example, openTab, insert, copy } };
        var help = new Expander
        {
            Header = "Pseudocode syntax",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new TextBlock { Text = CheatSheet, FontFamily = new FontFamily("Cascadia Mono,Consolas"), FontSize = 12 }
        };

        var root = new Grid { Margin = new Thickness(10), RowDefinitions = new RowDefinitions("*,Auto,Auto,Auto") };
        root.Children.Add(editors);
        _status.Margin = new Thickness(0, 8, 0, 0);
        Grid.SetRow(_status, 1);
        root.Children.Add(_status);
        Grid.SetRow(help, 2);
        help.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(help);
        Grid.SetRow(buttons, 3);
        buttons.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(buttons);
        Content = root;

        _timer.Tick += (_, _) => { _timer.Stop(); Regenerate(); };
        _input.TextChanged += (_, _) => { _timer.Stop(); _timer.Start(); };
        Opened += (_, _) => { Regenerate(); _input.Focus(); };
    }

    private TextEditor CreateEditor(bool readOnly, string colorizerName)
    {
        var editor = new TextEditor
        {
            IsReadOnly = readOnly,
            ShowLineNumbers = true,
            FontFamily = new FontFamily("Cascadia Mono,Consolas"),
            FontSize = 14,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        editor.Options.ConvertTabsToSpaces = true;
        editor.Options.IndentationSize = 4;
        editor.TextArea.TextView.LineTransformers.Add(new CodeColorizer(() => colorizerName, () => ActualThemeVariant));
        return editor;
    }

    private static Control Titled(string title, Control content, int column)
    {
        var panel = new DockPanel { [Grid.ColumnProperty] = column };
        var header = new TextBlock { Text = title, FontWeight = FontWeight.Bold, Margin = new Thickness(2, 0, 0, 4) };
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(new Border { BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, Child = content });
        return panel;
    }

    private void Regenerate()
    {
        var result = PseudocodeCompiler.Compile(_input.Text ?? string.Empty);
        _diagnostics.Set(result.Errors.Select(e => new EditorDiagnostic(e.Line, e.Column, e.Message, EditorDiagnosticSeverity.Error)));
        if (result.Success)
        {
            _assembly = result.Assembly;
            _output.Text = result.Assembly;
            var lines = result.Assembly.Split('\n').Count(l => l.StartsWith("    ") && !l.TrimStart().StartsWith('#'));
            _status.Text = $"OK: {lines} instruction line(s) generated.";
        }
        else
        {
            // Keep the last good assembly visible while the pseudocode is being fixed.
            _status.Text = string.Join("\n", result.Errors.Take(6).Select(e => $"Line {e.Line}, column {e.Column}: {e.Message}")) +
                           (result.Errors.Count > 6 ? $"\n...and {result.Errors.Count - 6} more." : string.Empty);
        }
    }
}
