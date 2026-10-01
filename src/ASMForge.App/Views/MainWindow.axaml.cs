using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Search;
using ASMForge.Core.Assembly;
using ASMForge.Core.Cpu;
using ASMForge.Core.Execution;
using ASMForge.Core.Memory;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using ASMForge.App;

namespace ASMForge.App.Views;

public partial class MainWindow : Window
{
    private readonly SimpleAssembler _assembler = new();
    // Separate assembler for live error checking while typing, so it never disturbs the assembled program.
    private readonly SimpleAssembler _checker = new();
    private readonly Avalonia.Threading.DispatcherTimer _checkTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private EditorDocument? _checkDocument;
    // The document whose source produced _program; runtime and assemble errors point into it.
    private EditorDocument? _assembledDocument;
    private readonly MipsMachine _machine = new();
    private AssemblyProgram? _program;
    private readonly List<EditorDocument> _documents = new();
    // Semi-transparent amber reads on both light and dark themes; changed values are also bolded.
    private static readonly IBrush ChangedBrush = new ImmutableSolidColorBrush(Color.FromArgb(110, 255, 196, 0));
    // Manual register/memory edits are highlighted in blue so they stand out from changes made by the program.
    private static readonly IBrush EditedBrush = new ImmutableSolidColorBrush(Color.FromArgb(110, 80, 150, 255));
    // Row i shows register change index i: 0-31, then HI (32) and LO (33). Commands are wired in the constructor.
    private readonly RegisterRow[] _registerRows = Enumerable.Range(0, RegisterFile.Count + 2).Select(i => new RegisterRow(i)).ToArray();
    // Memory view menu indices; Stack, $gp and $fp follow their register.
    private const int DataSegment = 0, HeapSegment = 1, StackSegment = 2, GpSegment = 3, FpSegment = 4, CustomSegment = 5;
    // What to do after the user enters input for a waiting read syscall.
    private enum ResumeMode { Run, Step }
    private ResumeMode _resumeMode = ResumeMode.Run;
    private int? _resumeRunToLine;
    private readonly MemoryRow[] _memoryRows = Enumerable.Range(0, 16).Select(_ => new MemoryRow()).ToArray();
    private AssemblyProgram? _textSegmentProgram;
    private List<TextRow> _textRows = new();
    private CompletionWindow? _completionWindow;
    private string? _projectFolder;
    private AppSettings _settings = AppSettings.Load();
    // True while restoring or tearing down, so tab changes don't overwrite the saved session.
    private bool _sessionPaused;
    // Set once the user has answered the unsaved-changes prompt, so the second close goes through.
    private bool _closeConfirmed;
    // Set while a MIPS program runs on a background thread; cancel it to Pause or Stop.
    private CancellationTokenSource? _runCts;
    private bool _stopRequested;
    // Copies console output to Run I/O while a program runs (registers/memory refresh when it stops).
    private readonly Avalonia.Threading.DispatcherTimer _runTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private const string Sample = "# ASMForge sample\nli $t0, 10\nli $t1, 3\nrem $t2, $t0, $t1\n\nmove $a0, $t2\nli $v0, 1\nsyscall\nli $v0, 10\nsyscall\n";
    private const string CsTemplate = """"
using System;
using ASMForge.Core.Execution;

namespace ASMForgeProject;

internal static class Program
{
    private const string AssemblySource = """
.data
message: .asciiz "Hello from simulated MIPS!\n"

.text
main:
    la $a0, message
    li $v0, 4
    syscall

    li $t0, 5
    li $t1, 6
    add $t2, $t0, $t1

    li $v0, 10
    syscall
""";

    private static void Main()
    {
        var mips = new ASMForgeRuntime();
        mips.LoadAssembly(AssemblySource);
        mips.Run();

        Console.Write(mips.Output);
        var t2 = mips.Registers["$t2"];
        Console.WriteLine($"$t2 = {t2}");
        Console.WriteLine($"PC = 0x{mips.PC:X8}");
    }
}
"""";
    // New C# files get a class named after the file, e.g. Testing.cs -> class Testing.
    private static string CsTemplateFor(string fileNameWithoutExtension) =>
        CsTemplate.Replace("internal static class Program", $"internal static class {CSharpRunner.ToClassName(fileNameWithoutExtension)}");

    private uint _memoryViewStart = MipsMemory.DataBase;

    public MainWindow()
    {
        DiagnosticLog.Info("MainWindow constructor started");
        InitializeComponent();
        DiagnosticLog.Info("MainWindow XAML initialized");
        ShowLineNumbersMenu.IsChecked = _settings.ShowLineNumbers;
        SyntaxHighlightingMenu.IsChecked = _settings.SyntaxHighlighting;
        CompileAllCSharpMenu.IsChecked = _settings.CompileAllCSharpFiles;
        // App shortcuts are handled before the editor sees them, so e.g. F3 assembles instead of "find next".
        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        _checkTimer.Tick += (_, _) =>
        {
            _checkTimer.Stop();
            if (_checkDocument is { } pending && _documents.Contains(pending)) RunLiveCheck(pending);
        };
        // ConsoleText is replaced (never mutated) by the simulator, so reading it from the UI thread is safe.
        _runTimer.Tick += (_, _) => SetConsoleText(_machine.ConsoleText);
        foreach (var row in _registerRows)
        {
            var index = row.Index;
            row.EditCommand = new RelayCommand(() => EditRegister(index));
            row.CopyNameCommand = new RelayCommand(() => CopyText(RegisterFile.NameOf(index)));
            row.CopyValueCommand = new RelayCommand(() => CopyText(FormatRegister(_machine.Registers.GetByIndex(index))));
            row.ShowInMemoryCommand = new RelayCommand(() => ShowInMemory(unchecked((uint)_machine.Registers.GetByIndex(index))));
        }
        // Restore the last Hex / Signed / Unsigned / Binary / ASCII choice for each viewer.
        RegisterFormat.SelectedIndex = Math.Clamp(_settings.RegisterFormatIndex, 0, RegisterFormat.ItemCount - 1);
        MemoryFormat.SelectedIndex = Math.Clamp(_settings.MemoryFormatIndex, 0, MemoryFormat.ItemCount - 1);
        RestoreSession();
        if (_documents.Count == 0) OpenDocument(null, "main.asm", Sample, false);
        RefreshExplorer(); RefreshDisplay();
        Opened += MainWindow_Opened;
        DiagnosticLog.Info($"MainWindow initialized. Documents={_documents.Count}, EditorTabs={EditorTabs.Items.Count}");
    }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        DiagnosticLog.Info("MainWindow Opened event fired");
        await Task.Delay(250);
        LogEditorDiagnostics("window-opened");
    }

    private void LogEditorDiagnostics(string reason)
    {
        try
        {
            DiagnosticLog.Info($"Editor diagnostics ({reason}): WindowBounds={Bounds.Width:0.##}x{Bounds.Height:0.##}; TabsBounds={EditorTabs.Bounds.Width:0.##}x{EditorTabs.Bounds.Height:0.##}; SelectedIndex={EditorTabs.SelectedIndex}; Items={EditorTabs.Items.Count}");
            var doc = ActiveDocument;
            if (doc is null) { DiagnosticLog.Warn("No active document/editor."); return; }
            var ed = doc.Editor;
            DiagnosticLog.Info($"ActiveDocument={doc.Name}; TextLength={ed.Text?.Length ?? 0}; EditorBounds={ed.Bounds.Width:0.##}x{ed.Bounds.Height:0.##}; IsVisible={ed.IsVisible}; IsEffectivelyVisible={ed.IsEffectivelyVisible}; Parent={ed.Parent?.GetType().FullName ?? "<null>"}; LineNumbers={ed.ShowLineNumbers}; SyntaxHighlighting={_settings.SyntaxHighlighting}; Transformers={ed.TextArea.TextView.LineTransformers.Count}; RequestedTheme={RequestedThemeVariant}");
        }
        catch (Exception ex) { DiagnosticLog.Error("Failed to collect editor diagnostics", ex); }
    }

    // Reopens the project folder and files that were open last time, then re-selects the active tab.
    private void RestoreSession()
    {
        _sessionPaused = true;
        var restored = 0;
        var missing = 0;
        try
        {
            if (_settings.ProjectFolder is { } folder)
            {
                if (Directory.Exists(folder)) _projectFolder = folder;
                else DiagnosticLog.Warn($"Previous project folder no longer exists: {folder}");
            }

            foreach (var path in _settings.OpenFiles)
            {
                if (!File.Exists(path)) { missing++; DiagnosticLog.Warn($"Previously open file no longer exists: {path}"); continue; }
                try { OpenFilePath(path); restored++; }
                catch (Exception ex) { missing++; DiagnosticLog.Error($"Could not reopen {path}", ex); }
            }

            var active = _documents.FindIndex(d => string.Equals(d.Path, _settings.ActiveFile, StringComparison.OrdinalIgnoreCase));
            if (active >= 0) EditorTabs.SelectedIndex = active;
        }
        finally
        {
            _sessionPaused = false;
        }
        SaveSession(); // drop files that no longer exist from the saved list

        if (restored > 0 || missing > 0)
            Status.Text = missing == 0 ? $"Restored {restored} file(s)" : $"Restored {restored} file(s); {missing} could not be found";
        DiagnosticLog.Info($"Session restored: project={_projectFolder ?? "<none>"}; files={restored}; missing={missing}");
    }

    // Persists the open project, open file paths and active file. Untitled documents have no path and are not saved.
    private void SaveSession()
    {
        if (_sessionPaused) return;
        _settings.ProjectFolder = _projectFolder;
        _settings.OpenFiles = _documents.Where(d => d.Path is not null).Select(d => d.Path!).ToList();
        _settings.ActiveFile = ActiveDocument?.Path;
        _settings.Save();
    }

    private EditorDocument? ActiveDocument => EditorTabs.SelectedIndex >= 0 && EditorTabs.SelectedIndex < _documents.Count ? _documents[EditorTabs.SelectedIndex] : null;
    private TextEditor? ActiveEditor => ActiveDocument?.Editor;

    private async void New_Click(object? s, RoutedEventArgs e)
    {
        var dialog = new NewItemDialog(_projectFolder); var ok = await dialog.ShowDialog<bool>(this);
        if (!ok || dialog.Result is null) return;
        try
        {
            var r = dialog.Result;
            if (r.Kind == 2)
            {
                // Creating a project replaces all open tabs, so offer to save first.
                if (!await ConfirmUnsavedAsync(_documents, "creating a new project")) return;
                var folder = Path.Combine(r.Location, r.Name); Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, r.Name + ".asmforge"), $"{{\n  \"name\": \"{r.Name.Replace("\"", "")}\",\n  \"version\": 1\n}}\n");
                var main = Path.Combine(folder, "main.asm");
                if (!File.Exists(main))
                    File.WriteAllText(main, "# " + r.Name + "\n.text\nmain:\n    li $v0, 10\n    syscall\n");
                var programCs = Path.Combine(folder, "Program.cs");
                if (!File.Exists(programCs))
                    File.WriteAllText(programCs, CsTemplate);
                _projectFolder = folder;
                CloseAllDocuments();
                OpenFilePath(main);
                OpenFilePath(programCs);
                RefreshExplorer();
                Status.Text = $"Created project {r.Name}";
            }
            else
            {
                var ext = r.Kind == 1 ? ".cs" : ".asm"; var name = r.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? r.Name : r.Name + ext;
                Directory.CreateDirectory(r.Location); var path = Path.Combine(r.Location, name);
                if (!File.Exists(path)) File.WriteAllText(path, r.Kind == 1 ? CsTemplateFor(Path.GetFileNameWithoutExtension(name)) : "# ASMForge assembly file\n.text\nmain:\n");
                OpenFilePath(path);
                RefreshExplorer();
                Status.Text = $"Created {name}";
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void Open_Click(object? s, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open source file", AllowMultiple = true, FileTypeFilter = new[]
        {
            // The first filter is the dialog's default, so show every ASMForge source type first.
            new FilePickerFileType("ASMForge source files (*.asm, *.s, *.cs)") { Patterns = new[] { "*.asm", "*.s", "*.cs" } },
            new FilePickerFileType("Assembly files (*.asm, *.s)") { Patterns = new[] { "*.asm", "*.s" } },
            new FilePickerFileType("C# files (*.cs)") { Patterns = new[] { "*.cs" } },
            FilePickerFileTypes.All
        } });
        foreach (var f in files) Try(() => OpenFilePath(f.Path.LocalPath));
    }
    private async void OpenFolder_Click(object? s, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open ASMForge project or folder", AllowMultiple = false });
        if (folders.Count == 0) return; _projectFolder = folders[0].Path.LocalPath; RefreshExplorer(); SaveSession(); Status.Text = $"Opened {Path.GetFileName(_projectFolder)}";
    }
    private void Save_Click(object? s, RoutedEventArgs e) => SaveActive(false);
    private void SaveAs_Click(object? s, RoutedEventArgs e) => SaveActive(true);
    private async void SaveActive(bool saveAs)
    {
        var doc = ActiveDocument;
        if (doc is not null) await SaveDocumentAsync(doc, saveAs);
    }

    /// <summary>Saves a document, asking for a path when it has none. Returns false if cancelled or failed.</summary>
    private async Task<bool> SaveDocumentAsync(EditorDocument doc, bool saveAs)
    {
        var path = doc.Path;
        if (saveAs || path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = $"Save {doc.Name}", SuggestedFileName = doc.Name });
            if (file is null) return false;
            path = file.Path.LocalPath;
        }
        try
        {
            File.WriteAllText(path, doc.Editor.Text ?? "");
            doc.Path = path; doc.Name = Path.GetFileName(path); doc.Dirty = false;
            UpdateTabHeaders(); RefreshExplorer(); SaveSession();
            Status.Text = $"Saved {doc.Name}";
            return true;
        }
        catch (Exception ex) { ShowError(ex); return false; }
    }

    // An empty, never-saved document has nothing worth keeping.
    private static bool NeedsSave(EditorDocument doc) => doc.Dirty && !(doc.Path is null && string.IsNullOrEmpty(doc.Editor.Text));

    /// <summary>
    /// Asks Save / Don't Save / Cancel for the given unsaved documents. Returns true when it is OK to continue
    /// (everything saved, or the user chose Don't Save), false when the user cancelled or a save was cancelled.
    /// </summary>
    private async Task<bool> ConfirmUnsavedAsync(IReadOnlyList<EditorDocument> documents, string action)
    {
        var unsaved = documents.Where(NeedsSave).ToList();
        if (unsaved.Count == 0) return true;

        var question = unsaved.Count == 1
            ? $"Save changes to {unsaved[0].Name} before {action}?"
            : $"Save changes to {unsaved.Count} files before {action}?";
        var details = string.Join("\n", unsaved.Select(d => $"• {d.Name}{(d.Path is null ? " (not saved yet)" : "")}"));
        switch (await SavePromptDialog.ShowAsync(this, question, details))
        {
            case SaveChoice.Cancel:
                Status.Text = "Cancelled";
                return false;
            case SaveChoice.DontSave:
                return true;
            default:
                foreach (var doc in unsaved)
                    if (!await SaveDocumentAsync(doc, saveAs: false)) return false;
                return true;
        }
    }

    private void CloseFile_Click(object? s, RoutedEventArgs e) => RequestCloseDocument(EditorTabs.SelectedIndex);

    // Closes a tab after offering to save it. The document is re-located after the prompt in case tabs changed.
    private async void RequestCloseDocument(int index)
    {
        if (index < 0 || index >= _documents.Count) return;
        var doc = _documents[index];
        if (!await ConfirmUnsavedAsync(new[] { doc }, "closing it")) return;
        CloseDocumentAt(_documents.IndexOf(doc));
    }

    private async void About_Click(object? s, RoutedEventArgs e) => await new AboutDialog().ShowDialog(this);

    private void UpdateTitle()
    {
        var doc = ActiveDocument;
        Title = doc is null ? AppInfo.DisplayName : $"{doc.Name}{(doc.Dirty ? " *" : "")} - {AppInfo.DisplayName}";
    }

    private void CloseDocumentAt(int index)
    {
        if (index < 0 || index >= _documents.Count) return;
        // Remove AvaloniaEdit's line-number margin before detaching the editor.
        // This avoids the known teardown path where the margin can render with an invalid font size.
        _documents[index].Editor.ShowLineNumbers = false;
        _documents.RemoveAt(index);
        EditorTabs.Items.RemoveAt(index);
        if (_documents.Count == 0)
            OpenDocument(null, "Untitled.asm", "", false);
        else
            EditorTabs.SelectedIndex = Math.Min(index, _documents.Count - 1);
        SaveSession();
    }

    private void OpenFilePath(string path)
    {
        var existing = _documents.FindIndex(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)); if (existing >= 0) { EditorTabs.SelectedIndex = existing; return; }
        OpenDocument(path, Path.GetFileName(path), File.ReadAllText(path), false);
    }
    private void OpenDocument(string? path, string name, string text, bool dirty)
    {
        DiagnosticLog.Info($"Opening document: {name}; path={path ?? "<untitled>"}; chars={text.Length}");

        var editor = new TextEditor
        {
            Text = text,
            ShowLineNumbers = _settings.ShowLineNumbers,
            FontFamily = new FontFamily("Cascadia Mono,Consolas"),
            FontSize = 15,
            WordWrap = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        editor.Options.ConvertTabsToSpaces = true;
        editor.Options.IndentationSize = 4;
        editor.Options.HighlightCurrentLine = true;

        if (_settings.SyntaxHighlighting)
            editor.TextArea.TextView.LineTransformers.Add(new CodeColorizer(() => name, () => ActualThemeVariant));

        editor.TextArea.KeyDown += (_, e) => HandleEditorIndent(editor, e);
        editor.TextArea.TextEntered += (_, e) => Editor_TextEntered(editor, name, e);
        editor.TextArea.TextEntering += Editor_TextEntering;

        // Breakpoint gutter (click to toggle); kept after line numbers so the order stays stable
        // when line numbers are toggled.
        var breakpoints = new BreakpointMargin();
        editor.TextArea.LeftMargins.Add(breakpoints);

        var editorBorder = new Border
        {
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(5),
            Margin = new Avalonia.Thickness(2, 0, 2, 2),
            Child = editor
        };

        ApplyEditorTheme(editor, editorBorder);

        DiagnosticLog.Info($"Editor created: {name}; syntax={_settings.SyntaxHighlighting}; transformers={editor.TextArea.TextView.LineTransformers.Count}; theme={RequestedThemeVariant}");

        // Red/yellow squiggles for errors and warnings, and the built-in Find/Replace panel.
        var diagnostics = new DiagnosticRenderer(editor.TextArea.TextView);
        editor.TextArea.TextView.BackgroundRenderers.Add(diagnostics);
        var search = SearchPanel.Install(editor);

        var doc = new EditorDocument(path, name, editor, editorBorder, breakpoints, diagnostics, search, dirty);
        editor.TextChanged += (_, _) =>
        {
            doc.Dirty = true;
            UpdateTabHeaders();
            _program = null;
            ScheduleLiveCheck(doc); // .asm: the file itself; .cs: assembly strings passed to LoadAssembly
        };
        editor.TextArea.Caret.PositionChanged += (_, _) => ShowDiagnosticAtCaret(doc);
        editor.TextArea.TextView.PointerMoved += (_, e) => UpdateDiagnosticTip(doc, e);

        _documents.Add(doc);
        var tab = new TabItem
        {
            Header = name,
            Content = editorBorder,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };
        tab.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(tab).Properties.PointerUpdateKind != PointerUpdateKind.MiddleButtonPressed) return;
            var index = EditorTabs.Items.IndexOf(tab);
            RequestCloseDocument(index);
            e.Handled = true;
        };

        EditorTabs.Items.Add(tab);
        EditorTabs.SelectedIndex = _documents.Count - 1;
        UpdateTabHeaders();
        editor.Focus();
        SaveSession();
        ScheduleLiveCheck(doc);
        // Measure the new editor immediately so its line-number margin has a valid font size
        // even if a menu popup closing right after this forces a render (see ShowLineNumbers_Click).
        if (IsVisible) UpdateLayout();

        DiagnosticLog.Info($"Editor attached to tab: {name}; tabs={EditorTabs.Items.Count}; editorParent={editor.Parent?.GetType().Name ?? "<null>"}");
        Avalonia.Threading.Dispatcher.UIThread.Post(() => LogEditorDiagnostics($"opened-{name}"), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void Editor_TextEntering(object? sender, TextInputEventArgs e)
    {
        if (_completionWindow is null || string.IsNullOrEmpty(e.Text))
            return;

        var ch = e.Text[0];
        if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '$' && ch != '.')
        {
            _completionWindow.Close();
            _completionWindow = null;
        }
    }

    private void Editor_TextEntered(TextEditor editor, string name, TextInputEventArgs e)
    {
        if (name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(e.Text))
            return;

        var ch = e.Text[0];
        if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '$' && ch != '.')
            return;

        ShowMipsCompletion(editor);
    }

    private void ShowMipsCompletion(TextEditor editor)
    {
        var prefix = GetCompletionPrefix(editor);
        if (prefix.Length == 0)
            return;

        var suggestions = MipsCompletionCatalog.GetSuggestions(prefix, editor.Text ?? string.Empty);
        if (suggestions.Count == 0)
        {
            _completionWindow?.Close();
            _completionWindow = null;
            return;
        }

        _completionWindow?.Close();
        var window = new CompletionWindow(editor.TextArea);
        _completionWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_completionWindow, window))
                _completionWindow = null;
        };

        foreach (var suggestion in suggestions)
            window.CompletionList.CompletionData.Add(suggestion);

        window.Show();
    }

    private static string GetCompletionPrefix(TextEditor editor)
    {
        var document = editor.Document;
        if (document is null)
            return string.Empty;

        var caret = Math.Clamp(editor.CaretOffset, 0, document.TextLength);
        var start = caret;
        while (start > 0)
        {
            var c = document.GetCharAt(start - 1);
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '$' && c != '.')
                break;
            start--;
        }

        return document.GetText(start, caret - start);
    }

    private void ApplyEditorTheme(TextEditor editor, Border border)
    {
        var dark = RequestedThemeVariant == ThemeVariant.Dark ||
                   (RequestedThemeVariant == ThemeVariant.Default && ActualThemeVariant == ThemeVariant.Dark);

        editor.Background = new SolidColorBrush(dark ? Color.FromRgb(18, 18, 18) : Color.FromRgb(255, 255, 255));
        editor.Foreground = new SolidColorBrush(dark ? Color.FromRgb(225, 225, 225) : Color.FromRgb(30, 30, 30));
        editor.LineNumbersForeground = new SolidColorBrush(dark ? Color.FromRgb(150, 150, 150) : Color.FromRgb(100, 100, 100));

        // Softer selection colors than the default bright system-blue block.
        editor.TextArea.SelectionBrush = new SolidColorBrush(dark ? Color.FromArgb(155, 58, 86, 118) : Color.FromArgb(170, 174, 207, 235));
        editor.TextArea.SelectionForeground = new SolidColorBrush(dark ? Colors.White : Color.FromRgb(20, 20, 20));
        editor.TextArea.Caret.CaretBrush = new SolidColorBrush(dark ? Colors.White : Colors.Black);

        editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(dark ? Color.FromArgb(75, 255, 255, 255) : Color.FromArgb(35, 0, 0, 0));
        editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(dark ? Color.FromRgb(70, 70, 70) : Color.FromRgb(215, 215, 215)));

        border.BorderBrush = new SolidColorBrush(dark ? Color.FromRgb(95, 95, 95) : Color.FromRgb(145, 145, 145));
        border.Background = editor.Background;
        editor.TextArea.TextView.Redraw();
    }

    private static void HandleEditorIndent(TextEditor editor, KeyEventArgs e)
    {
        if (e.Key != Key.Tab) return;
        var area = editor.TextArea; var doc = editor.Document; if (doc is null) return;
        var startLine = doc.GetLineByOffset(area.Selection.SurroundingSegment.Offset).LineNumber;
        var endLine = doc.GetLineByOffset(area.Selection.SurroundingSegment.EndOffset).LineNumber;
        if (area.Selection.IsEmpty) return; // AvaloniaEdit handles a normal Tab itself.
        doc.BeginUpdate();
        try
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                for (var lineNo = startLine; lineNo <= endLine; lineNo++) { var line = doc.GetLineByNumber(lineNo); var remove = 0; while (remove < Math.Min(4, line.Length) && doc.GetCharAt(line.Offset + remove) == ' ') remove++; if (remove > 0) doc.Remove(line.Offset, remove); else if (line.Length > 0 && doc.GetCharAt(line.Offset) == '\t') doc.Remove(line.Offset, 1); }
            }
            else for (var lineNo = startLine; lineNo <= endLine; lineNo++) doc.Insert(doc.GetLineByNumber(lineNo).Offset, "    ");
        }
        finally { doc.EndUpdate(); }
        e.Handled = true;
    }
    private void CloseAllDocuments() { _documents.Clear(); EditorTabs.Items.Clear(); }
    private void UpdateTabHeaders() { for (var i = 0; i < _documents.Count && i < EditorTabs.Items.Count; i++) if (EditorTabs.Items[i] is TabItem t) t.Header = _documents[i].Name + (_documents[i].Dirty ? " *" : ""); UpdateTitle(); }
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules" };

    /// <summary>
    /// Files under a project folder, skipping build output, version control and hidden folders. Folders that
    /// cannot be read (deleted, no permission) are skipped instead of failing the whole scan.
    /// </summary>
    private static IEnumerable<string> EnumerateProjectFiles(string root, string pattern)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            string[] files, folders;
            try
            {
                files = Directory.GetFiles(folder, pattern);
                folders = Directory.GetDirectories(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DiagnosticLog.Warn($"Skipping unreadable folder {folder}: {ex.Message}");
                continue;
            }
            foreach (var file in files) yield return file;
            foreach (var sub in folders)
            {
                var name = Path.GetFileName(sub);
                if (!SkippedFolders.Contains(name) && !name.StartsWith('.')) pending.Push(sub);
            }
        }
    }

    private void RefreshExplorer()
    {
        if (ExplorerList is null || ProjectNameText is null)
            return;

        var items = new List<string>();
        if (_projectFolder is not null && Directory.Exists(_projectFolder))
        {
            ProjectNameText.Text = GetProjectDisplayName(_projectFolder);
            foreach (var file in EnumerateProjectFiles(_projectFolder, "*")
                         .Where(p => p.EndsWith(".asm", StringComparison.OrdinalIgnoreCase) ||
                                     p.EndsWith(".s", StringComparison.OrdinalIgnoreCase) ||
                                     p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                items.Add(Path.GetRelativePath(_projectFolder, file));
            }
        }
        else
        {
            ProjectNameText.Text = "No project open";
            items.Add("No project/folder open");
        }

        ExplorerList.ItemsSource = items;
    }

    private static string GetProjectDisplayName(string folder)
    {
        try
        {
            var projectFile = Directory.EnumerateFiles(folder, "*.asmforge", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (projectFile is not null)
            {
                using var json = JsonDocument.Parse(File.ReadAllText(projectFile));
                if (json.RootElement.TryGetProperty("name", out var nameElement))
                {
                    var configuredName = nameElement.GetString();
                    if (!string.IsNullOrWhiteSpace(configuredName))
                        return configuredName;
                }
            }
        }
        catch
        {
            // Fall back to the folder name if project metadata is unavailable or malformed.
        }

        return Path.GetFileName(folder);
    }

    private void ExplorerList_DoubleTapped(object? s, TappedEventArgs e) { if (_projectFolder is null || ExplorerList.SelectedItem is not string item) return; var rel = item.Trim(); if (rel == "No project/folder open") return; var path = Path.Combine(_projectFolder, rel); if (File.Exists(path)) Try(() => OpenFilePath(path)); else RefreshExplorer(); }
    private void EditorTabs_SelectionChanged(object? s, SelectionChangedEventArgs e) { _program = null; SaveSession(); UpdateTitle(); Status.Text = ActiveDocument is null ? "Ready" : ActiveDocument.Name; ActiveEditor?.Focus(); DiagnosticLog.Info($"Editor tab changed: index={EditorTabs.SelectedIndex}, active={ActiveDocument?.Name ?? "<none>"}"); Avalonia.Threading.Dispatcher.UIThread.Post(() => LogEditorDiagnostics("tab-selection-changed"), Avalonia.Threading.DispatcherPriority.Loaded); }
    private void ShowLineNumbers_Click(object? s, RoutedEventArgs e)
    {
        _settings.ShowLineNumbers = ShowLineNumbersMenu.IsChecked;
        _settings.Save();

        // Closing the Settings menu popup forces an immediate render. If a newly added
        // AvaloniaEdit LineNumberMargin is rendered before it has been measured, its font size
        // is still 0 and FormattedText throws "emSize must be greater than zero".
        // So apply the change after the popup has closed, then measure the new margins at once.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                foreach (var d in _documents) d.Editor.ShowLineNumbers = _settings.ShowLineNumbers;
                UpdateLayout();
            }
            catch (Exception ex) { DiagnosticLog.Error("Failed to toggle line numbers", ex); }
        });
    }
    private void CompileAllCSharp_Click(object? s, RoutedEventArgs e)
    {
        _settings.CompileAllCSharpFiles = CompileAllCSharpMenu.IsChecked;
        _settings.Save();
        Status.Text = _settings.CompileAllCSharpFiles ? "Run compiles all project C# files" : "Run compiles only the active C# file";
    }

    private void SyntaxHighlighting_Click(object? s, RoutedEventArgs e)
    {
        _settings.SyntaxHighlighting = SyntaxHighlightingMenu.IsChecked;
        foreach (var d in _documents)
        {
            d.Editor.TextArea.TextView.LineTransformers.Clear();
            if (_settings.SyntaxHighlighting)
                d.Editor.TextArea.TextView.LineTransformers.Add(new CodeColorizer(() => d.Name, () => ActualThemeVariant));
            d.Editor.TextArea.TextView.Redraw();
        }
        _settings.Save();
        DiagnosticLog.Info($"Syntax highlighting toggled: {_settings.SyntaxHighlighting}");
    }

    private void Assemble()
    {
        var editor = ActiveEditor ?? throw new InvalidOperationException("No file is open.");
        if (ActiveDocument?.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("C# host code is not compiled by the ASM editor. The generated template now uses the public ASMForgeRuntime API; run it from a .NET project that references ASMForge.Core.");

        var source = editor.Text ?? string.Empty;
        _assembledDocument = ActiveDocument;
        _program = _assembler.Assemble(source);
        if (_assembledDocument is not null) RunLiveCheck(_assembledDocument);
        _machine.Load(_program); // also clears Step Back history

        var warnings = GetAssemblyWarnings(source);
        Status.Text = warnings.Count == 0
            ? $"Assembled {_program.Instructions.Count} basic instruction(s)"
            : $"Assembled with {warnings.Count} warning(s)";

        Messages.Text = $"Assemble completed successfully.\n{_program.Instructions.Count} basic instruction(s) generated." +
                        (warnings.Count == 0 ? string.Empty : "\n\nWarnings:\n" + string.Join("\n", warnings));
        Console.Text = string.Empty;
        OutputTabs.SelectedIndex = 1;
        WorkspaceTabs.SelectedIndex = 1;
        RefreshDisplay();
    }

    private static List<string> GetAssemblyWarnings(string source)
    {
        var warnings = new List<string>();
        var lines = source.Replace("\r", string.Empty).Split('\n');
        var numericRegister = new Regex(@"\$(?<n>\d{1,2})\b", RegexOptions.Compiled);

        for (var i = 0; i < lines.Length; i++)
        {
            var code = lines[i].Split('#')[0];
            foreach (Match match in numericRegister.Matches(code))
            {
                if (!int.TryParse(match.Groups["n"].Value, out var number) || number is < 0 or > 31)
                    continue;
                warnings.Add($"Line {i + 1}: {match.Value} is numeric register {number}, which is {RegisterFile.Names[number]}.");
            }
        }
        return warnings;
    }
    private void Assemble_Click(object? s, RoutedEventArgs e)
    {
        if (BlockWhileRunning()) return;
        if (ActiveDocument?.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
        {
            Status.Text = "C# files are compiled when you press Run";
            Messages.Text = _settings.CompileAllCSharpFiles
                ? "C# source detected. Press Run (F5) to compile all C# files in the current project and execute this file's Main method."
                : "C# source detected. Press Run (F5) to compile this file and execute its Main method. To compile all project C# files together, enable Settings > Run All Project C# Files Together.";
            OutputTabs.SelectedIndex = 0;
            return;
        }
        Try(Assemble);
    }

    private void Step_Click(object? s, RoutedEventArgs e)
    {
        if (BlockWhileRunning()) return;
        if (ActiveDocument?.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
        {
            Messages.Text = "Instruction stepping applies to MIPS assembly. Run the C# host, then use ASMForgeRuntime.Step() inside C# when host-controlled stepping is needed.";
            OutputTabs.SelectedIndex = 0;
            Status.Text = "C# host stepping is API-controlled";
            return;
        }
        StepOnce();
    }

    private void StepOnce() => Try(() =>
    {
        if (_program is null) Assemble();
        _machine.Step();
        RefreshDisplay();
        if (_machine.WaitingForInput) PromptForInput(ResumeMode.Step);
        else Status.Text = _machine.Halted ? "Finished" : "Stepped";
    });

    // ---- Console input for read syscalls (5, 8, 12) ----

    private static string DescribeInput(InputKind? kind) => kind switch
    {
        InputKind.Integer => "an integer",
        InputKind.String => "a line of text",
        InputKind.Character => "a character",
        _ => "input"
    };

    private void PromptForInput(ResumeMode mode, int? runToLine = null)
    {
        _resumeMode = mode;
        _resumeRunToLine = runToLine;
        OutputTabs.SelectedIndex = 1;
        Status.Text = $"Waiting for {DescribeInput(_machine.PendingInputKind)}. Type it in Run I/O and press Enter.";
        UpdateInputState();
        InputBox.Focus();
    }

    private void UpdateInputState()
    {
        if (InputBox is null) return;
        var waiting = _machine.WaitingForInput && !IsRunning;
        InputBox.IsEnabled = waiting;
        InputSendButton.IsEnabled = waiting;
        InputPrompt.Text = waiting ? $"Enter {DescribeInput(_machine.PendingInputKind)}:" : "Input";
    }

    private void InputBox_KeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        SubmitInput();
    }

    private void InputSend_Click(object? s, RoutedEventArgs e) => SubmitInput();

    // Sends the typed line to the waiting read syscall, then resumes the way execution was going (Run or Step).
    private async void SubmitInput()
    {
        if (!_machine.WaitingForInput || IsRunning) return;
        var text = InputBox.Text ?? string.Empty;
        if (_machine.PendingInputKind == InputKind.Integer &&
            !int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            Status.Text = $"\"{text}\" is not a valid integer. Enter a whole number such as 42 or -7.";
            InputBox.SelectAll();
            return;
        }

        _machine.ProvideInput(text);
        InputBox.Text = string.Empty;
        UpdateInputState();
        if (_resumeMode == ResumeMode.Step) StepOnce();
        else await RunMipsAsync(_resumeRunToLine);
    }

    private void SetConsoleText(string text)
    {
        if (Console.Text == text) return;
        Console.Text = text;
        Console.CaretIndex = text.Length; // keep the latest output in view
    }

    // ---- Register / memory editing and context-menu actions ----

    private void RegistersList_DoubleTapped(object? s, TappedEventArgs e)
    {
        if ((e.Source as Avalonia.StyledElement)?.DataContext is RegisterRow row) EditRegister(row.Index);
    }

    private void MemoryGrid_DoubleTapped(object? s, TappedEventArgs e)
    {
        if ((e.Source as Avalonia.StyledElement)?.DataContext is MemoryCell cell) EditMemoryWord(cell.Address);
    }

    private async void EditRegister(int index)
    {
        if (BlockWhileRunning()) return;
        var name = RegisterFile.NameOf(index);
        if (index == 0) { Status.Text = "$zero is always 0 and cannot be edited"; return; }
        var value = await EditValueDialog.ShowAsync(this, $"Edit {name}", unchecked((uint)_machine.Registers.GetByIndex(index)));
        if (value is not { } newValue) return;
        Try(() =>
        {
            _machine.EditRegister(index, unchecked((int)newValue));
            RefreshDisplay();
            Status.Text = $"{name} set to 0x{newValue:X8}. Step Back (F9) undoes the edit.";
        });
    }

    private async void EditMemoryWord(uint address)
    {
        if (BlockWhileRunning()) return;
        var value = await EditValueDialog.ShowAsync(this, $"Edit word at 0x{address:X8}", _machine.Memory.ReadWordUnsigned(address));
        if (value is not { } newValue) return;
        Try(() =>
        {
            _machine.EditMemoryWord(address, newValue);
            RefreshDisplay();
            Status.Text = $"Word at 0x{address:X8} set to 0x{newValue:X8}. Step Back (F9) undoes the edit.";
        });
    }

    private async void CopyText(string text)
    {
        try
        {
            if (Clipboard is null) return;
            await Clipboard.SetTextAsync(text);
            Status.Text = $"Copied {(text.Length > 60 ? text[..60] + "…" : text)}";
        }
        catch (Exception ex) { DiagnosticLog.Error("Copy to clipboard failed", ex); }
    }

    // Shows the memory around an address (a register's value or a pointer stored in memory).
    private void ShowInMemory(uint address)
    {
        _memoryViewStart = address & 0xfffffff0u;
        if (MemorySegment is not null) MemorySegment.SelectedIndex = CustomSegment;
        if (MemoryAddressBox is not null) MemoryAddressBox.Text = $"0x{_memoryViewStart:X8}";
        WorkspaceTabs.SelectedIndex = 1;
        RefreshMemoryViewer();
        Status.Text = $"Showing memory at 0x{address:X8}";
    }

    private string MemoryRowText(uint rowStart)
    {
        var words = Enumerable.Range(0, 4).Select(i => FormatMemoryWord(_machine.Memory.ReadWordUnsigned(rowStart + (uint)(i * 4))));
        return $"0x{rowStart:X8}  {string.Join("  ", words)}  {MemoryAscii(rowStart)}";
    }

    private string MemoryAscii(uint rowStart) => new(Enumerable.Range(0, 16)
        .Select(i => _machine.Memory.ReadByte(rowStart + (uint)i))
        .Select(b => b is >= 32 and <= 126 ? (char)b : '.').ToArray());

    private async void Run_Click(object? s, RoutedEventArgs e)
    {
        if (BlockWhileRunning()) return;
        if (ActiveDocument?.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
        {
            await RunCSharpAsync();
            return;
        }
        await RunMipsAsync(null);
    }

    private async void RunToCursor_Click(object? s, RoutedEventArgs e)
    {
        if (BlockWhileRunning()) return;
        var editor = ActiveEditor;
        if (editor is null || ActiveDocument?.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
        {
            Status.Text = "Run to Cursor applies to MIPS assembly";
            return;
        }
        await RunMipsAsync(editor.TextArea.Caret.Line);
    }

    private void Pause_Click(object? s, RoutedEventArgs e)
    {
        if (_runCts is null) { Status.Text = "Nothing is running"; return; }
        Status.Text = "Pausing…";
        _runCts.Cancel();
    }

    private void Stop_Click(object? s, RoutedEventArgs e)
    {
        if (_runCts is not null)
        {
            // RunMipsAsync resets the program once the background run has actually stopped.
            _stopRequested = true;
            Status.Text = "Stopping…";
            _runCts.Cancel();
            return;
        }
        if (ActiveDocument?.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true) return;
        Try(Assemble);
        Status.Text = "Stopped";
    }

    private void ToggleBreakpoint_Click(object? s, RoutedEventArgs e)
    {
        var doc = ActiveDocument;
        if (doc is null) return;
        var line = doc.Editor.TextArea.Caret.Line;
        doc.Breakpoints.Toggle(line);
        Status.Text = doc.Breakpoints.HasBreakpoint(line) ? $"Breakpoint set on line {line}" : $"Breakpoint removed from line {line}";
    }

    private void ClearBreakpoints_Click(object? s, RoutedEventArgs e)
    {
        foreach (var doc in _documents) doc.Breakpoints.Clear();
        Status.Text = "All breakpoints cleared";
    }

    // Instruction limit for one Run/Continue; Pause is available while it runs.
    private const int MaxRunInstructions = 10_000_000;

    /// <summary>
    /// Runs the assembled program on a background thread until it halts, hits a breakpoint,
    /// reaches <paramref name="runToLine"/>, or is paused/stopped. The UI stays responsive and
    /// Run I/O updates live; registers and memory refresh when the run stops.
    /// </summary>
    private async Task RunMipsAsync(int? runToLine)
    {
        try { if (_program is null) Assemble(); }
        catch (Exception ex) { ShowError(ex); return; }
        var program = _program!;

        if (_machine.Halted)
        {
            Status.Text = "Program finished. Press Reset to run it again.";
            return;
        }

        uint? runTo = null;
        if (runToLine is int line)
        {
            var target = program.Instructions.FirstOrDefault(x => x.Line == line);
            if (target is null)
            {
                Status.Text = $"Line {line} has no instruction to run to";
                return;
            }
            runTo = target.Address;
        }

        var breakpoints = BreakpointAddresses(program, out var skipped);
        var cts = new CancellationTokenSource();
        _runCts = cts;
        _stopRequested = false;
        SetRunningUi(true);
        Status.Text = "Running…";
        _runTimer.Start();

        StopReason reason = StopReason.Halted;
        Exception? error = null;
        try
        {
            reason = await Task.Run(() => _machine.RunUntil(MaxRunInstructions, breakpoints, runTo, cts.Token));
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            _runTimer.Stop();
            _runCts = null;
            cts.Dispose();
            SetRunningUi(false);
        }

        if (error is not null) { ShowError(error); return; }
        if (_stopRequested)
        {
            Try(Assemble);
            Status.Text = "Stopped";
            return;
        }

        WorkspaceTabs.SelectedIndex = 1;
        RefreshDisplay();
        if (reason == StopReason.WaitingForInput)
        {
            PromptForInput(ResumeMode.Run, runToLine);
            return;
        }
        var where = CurrentSourceLineText();
        Status.Text = reason switch
        {
            StopReason.Breakpoint => $"Breakpoint hit{where}. Press Run (F5) to continue.",
            StopReason.RunToTarget => $"Reached cursor{where}",
            StopReason.Paused => $"Paused{where}",
            StopReason.LimitReached => $"Paused after {MaxRunInstructions:N0} instructions{where} (possible infinite loop). Press Run to continue.",
            _ => "Finished"
        };
        if (skipped > 0)
        {
            Messages.Text = $"{skipped} breakpoint(s) are on lines with no instruction (comments, labels, data) and were ignored.";
        }
    }

    private string CurrentSourceLineText()
    {
        if (_program is null || _machine.Halted || _machine.InstructionIndex >= _program.Instructions.Count) return string.Empty;
        return $" at line {_program.Instructions[_machine.InstructionIndex].Line} (0x{_machine.PC:X8})";
    }

    // Maps the active editor's breakpoint lines to the address of the first instruction on each line.
    private HashSet<uint> BreakpointAddresses(AssemblyProgram program, out int skipped)
    {
        var result = new HashSet<uint>();
        skipped = 0;
        var doc = ActiveDocument;
        if (doc is null) return result;
        foreach (var line in doc.Breakpoints.Lines)
        {
            var instruction = program.Instructions.FirstOrDefault(x => x.Line == line);
            if (instruction is null) skipped++;
            else result.Add(instruction.Address);
        }
        return result;
    }

    private bool IsRunning => _runCts is not null;

    private bool BlockWhileRunning()
    {
        if (!IsRunning) return false;
        Status.Text = "Program is running. Pause (F6) or Stop (Shift+F5) first.";
        return true;
    }

    private void SetRunningUi(bool running)
    {
        PauseButton.IsEnabled = running;
        StopButton.IsEnabled = running;
        RunButton.IsEnabled = !running;
        StepButton.IsEnabled = !running;
        BackButton.IsEnabled = !running;
        AssembleButton.IsEnabled = !running;
        ResetButton.IsEnabled = !running;
    }

    private void Reset_Click(object? s, RoutedEventArgs e)
    {
        if (BlockWhileRunning()) return;
        if (ActiveDocument?.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
        {
            Console.Text = string.Empty;
            Messages.Text = "C# host output cleared. Each Run performs a fresh compilation and execution.";
            Status.Text = "C# reset";
            return;
        }
        Try(Assemble);
    }
    private void Back_Click(object? s, RoutedEventArgs e)
    {
        if (BlockWhileRunning()) return;
        if (ActiveDocument?.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
        {
            Messages.Text = "Step Back applies to MIPS assembly. In C# host code, use ASMForgeRuntime.StepBack().";
            OutputTabs.SelectedIndex = 0;
            Status.Text = "C# host stepping is API-controlled";
            return;
        }
        if (_program is null)
        {
            // The source changed (or another tab was selected) since the last assemble,
            // so the recorded history no longer matches the editor.
            Messages.Text = "Step Back needs the current source to be assembled. Assemble (F3) and step again.";
            OutputTabs.SelectedIndex = 0;
            Status.Text = "Assemble first";
            return;
        }
        Try(() =>
        {
            Status.Text = _machine.StepBack() ? $"Stepped back to 0x{_machine.PC:X8}" : "Nothing to step back";
            RefreshDisplay();
        });
    }

    private async Task RunCSharpAsync()
    {
        try
        {
            var sources = CollectCSharpSources();
            Status.Text = $"Compiling {sources.Count} C# file(s)...";
            Messages.Text = string.Empty;
            Console.Text = string.Empty;
            OutputTabs.SelectedIndex = 1;

            var result = await CSharpRunner.CompileAndRunAsync(sources, ActiveDocument?.Path);
            Console.Text = result.Output;
            Messages.Text = result.Diagnostics;
            ApplyCSharpDiagnostics(result.Diagnostics);
            if (result.Success)
            {
                Status.Text = "C# finished";
                OutputTabs.SelectedIndex = 1;
            }
            else
            {
                Status.Text = "C# compile/runtime error";
                OutputTabs.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private List<CSharpSource> CollectCSharpSources()
    {
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var active = ActiveDocument;
        var activeIsProjectMember = active?.Path is not null && IsPathInsideProject(active.Path);

        // By default only the active C# file is compiled. With "Run All Project C# Files Together"
        // on, a C# file inside the open project compiles with all C# project files; the active
        // file's Main is used as the entry point. A loose C# file always compiles by itself.
        if (_settings.CompileAllCSharpFiles && activeIsProjectMember && _projectFolder is not null && Directory.Exists(_projectFolder))
        {
            foreach (var path in EnumerateProjectFiles(_projectFolder, "*.cs"))
            {
                sources[path] = File.ReadAllText(path);
            }

            foreach (var doc in _documents.Where(d => d.Path is not null &&
                                                       d.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                                                       IsPathInsideProject(d.Path)))
            {
                sources[doc.Path!] = doc.Editor.Text ?? string.Empty;
            }
        }
        else if (active is not null && active.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            sources[active.Path ?? $"<memory>/{active.Name}"] = active.Editor.Text ?? string.Empty;
        }

        return sources.Select(kv => new CSharpSource(kv.Key, kv.Value)).ToList();
    }

    private bool IsPathInsideProject(string path)
    {
        if (_projectFolder is null) return false;
        var root = Path.GetFullPath(_projectFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(path);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }


    private void RegisterFormat_SelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        if (RegisterFormat is not null && RegisterFormat.SelectedIndex >= 0) { _settings.RegisterFormatIndex = RegisterFormat.SelectedIndex; _settings.Save(); }
        if (RegistersList is not null) RefreshDisplay();
    }

    private void MemoryFormat_SelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        if (MemoryFormat is not null && MemoryFormat.SelectedIndex >= 0) { _settings.MemoryFormatIndex = MemoryFormat.SelectedIndex; _settings.Save(); }
        if (MemoryGrid is not null) RefreshMemoryViewer();
    }
    private void ThemeMode_SelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        if (ThemeMode is null) return;
        RequestedThemeVariant = ThemeMode.SelectedIndex switch
        {
            1 => ThemeVariant.Light,
            2 => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        foreach (var d in _documents)
        {
            ApplyEditorTheme(d.Editor, d.EditorBorder);
            d.Editor.TextArea.TextView.Redraw();
        }
    }
    // Registered as a tunnel handler, so these run before the focused control (e.g. the editor) handles the key.
    private void Window_KeyDown(object? s, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var inEditor = ActiveEditor?.TextArea.IsKeyboardFocusWithin == true;
        var a = new RoutedEventArgs();
        Action? action = null;

        if (ctrl && e.Key == Key.N) action = () => New_Click(s, a);
        else if (ctrl && e.Key == Key.O) action = shift ? () => OpenFolder_Click(s, a) : () => Open_Click(s, a);
        else if (ctrl && e.Key == Key.S) action = shift ? () => SaveAs_Click(s, a) : () => Save_Click(s, a);
        else if (ctrl && e.Key == Key.W) action = () => CloseFile_Click(s, a);
        else if (ctrl && e.Key == Key.F) action = () => Find_Click(s, a);
        else if (ctrl && e.Key == Key.H) action = () => Replace_Click(s, a);
        else if (ctrl && e.Key == Key.G) action = () => GoToLine_Click(s, a);
        else if (ctrl && e.Key == Key.R) action = () => GoToLabel_Click(s, a);
        else if (ctrl && e.Key == Key.B) action = () => ToggleBreakpoint_Click(s, a);
        else if (inEditor && ctrl && e.Key is Key.OemQuestion or Key.Divide) action = () => ToggleComment_Click(s, a);
        else if (inEditor && ctrl && e.Key == Key.D) action = () => DuplicateLine_Click(s, a);
        else if (inEditor && alt && e.Key == Key.Up) action = () => MoveLineUp_Click(s, a);
        else if (inEditor && alt && e.Key == Key.Down) action = () => MoveLineDown_Click(s, a);
        else if (e.Key == Key.F3) action = () => Assemble_Click(s, a);
        else if (shift && e.Key == Key.F5) action = () => Stop_Click(s, a);
        else if (e.Key == Key.F5) action = () => Run_Click(s, a);
        else if (e.Key == Key.F6) action = () => Pause_Click(s, a);
        else if (e.Key == Key.F9) action = () => Back_Click(s, a);
        else if (ctrl && e.Key == Key.F10) action = () => RunToCursor_Click(s, a);
        else if (e.Key == Key.F10) action = () => Step_Click(s, a);

        if (action is null) return;
        e.Handled = true;
        action();
    }
    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        // Unsaved work: cancel this close, ask, and close again only if the user agrees.
        // e.Cancel must be set before the first await.
        if (!_closeConfirmed && _documents.Any(NeedsSave))
        {
            e.Cancel = true;
            if (await ConfirmUnsavedAsync(_documents, "closing ASMForge"))
            {
                _closeConfirmed = true;
                Close();
            }
            return;
        }

        // AvaloniaEdit 11.x can try to render its line-number margin while the
        // window is being torn down. At that point inherited font metrics may
        // already be invalid, which can produce an emSize <= 0 exception.
        // Remove the line-number margins before the compositor disposes them.
        try
        {
            // Save the session first, then pause saving so teardown tab changes can't overwrite it.
            SaveSession();
            _sessionPaused = true;
            _runCts?.Cancel(); // let a running program's background thread stop
            _completionWindow?.Close();
            _completionWindow = null;
            foreach (var document in _documents)
            {
                document.Editor.ShowLineNumbers = false;
                if (document.Editor.FontSize <= 0) document.Editor.FontSize = 15;
            }
            DiagnosticLog.Info("Window closing: line-number margins disabled before editor teardown.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Editor teardown preparation failed: {ex.Message}");
        }
    }

    private void Try(Action a) { try { a(); } catch (Exception ex) { ShowError(ex); } }
    private void ShowError(Exception ex)
    {
        DiagnosticLog.Error("UI operation failed", ex);
        Status.Text = "Error";
        Messages.Text = ex.Message;
        OutputTabs.SelectedIndex = 0;
        MarkErrorLine(ex.Message);
        RefreshDisplay();
    }

    // ---- Diagnostics: live squiggles, hover/caret messages, Messages navigation ----

    private static bool IsAssemblyDocument(EditorDocument doc) => !doc.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    private void ScheduleLiveCheck(EditorDocument doc)
    {
        _checkDocument = doc;
        _checkTimer.Stop();
        _checkTimer.Start();
    }

    // Assembles the document's text in check-only mode and squiggles every error and warning.
    // For C# files, the assembly inside strings passed to LoadAssembly is checked instead.
    private void RunLiveCheck(EditorDocument doc)
    {
        try
        {
            var source = doc.Editor.Text ?? string.Empty;
            if (!IsAssemblyDocument(doc))
            {
                doc.Diagnostics.Set(EmbeddedAssemblyChecker.Check(source, _checker, GetAssemblyWarnings));
                if (doc == ActiveDocument) ShowDiagnosticAtCaret(doc);
                return;
            }
            var diagnostics = _checker.CheckAll(source)
                .Where(e => e.Line > 0)
                .Select(e => new EditorDiagnostic(e.Line, null, e.Message, EditorDiagnosticSeverity.Error))
                .ToList();
            diagnostics.AddRange(GetAssemblyWarnings(source)
                .Select(w => DiagnosticRenderer.FromAssemblyMessage(w, EditorDiagnosticSeverity.Warning))
                .OfType<EditorDiagnostic>());
            doc.Diagnostics.Set(diagnostics);
            if (doc == ActiveDocument) ShowDiagnosticAtCaret(doc);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("Live error check failed", ex);
        }
    }

    // After an assemble or runtime error ("Line N: ..."), squiggle the offending line in the assembled document.
    private void MarkErrorLine(string message)
    {
        var doc = _assembledDocument ?? ActiveDocument;
        if (doc is null || !_documents.Contains(doc) || !IsAssemblyDocument(doc)) return;
        if (DiagnosticRenderer.FromAssemblyMessage(message) is not { } error) return;

        RunLiveCheck(doc); // assemble errors: shows every bad line, not just the first
        if (doc.Diagnostics.At(error.Line)?.Severity != EditorDiagnosticSeverity.Error)
            doc.Diagnostics.Set(doc.Diagnostics.Diagnostics.Append(error).ToList()); // runtime error on a valid line
        Status.Text = $"Error on line {error.Line} (double-click it in Messages to jump there)";
    }

    private void ApplyCSharpDiagnostics(string diagnosticsText)
    {
        var byFile = DiagnosticRenderer.FromCSharpDiagnostics(diagnosticsText)
            .GroupBy(d => d.File, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(d => d.Diagnostic).ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var doc in _documents.Where(d => !IsAssemblyDocument(d)))
        {
            // Compiler errors from this run, plus the live check of assembly embedded in the file's strings.
            var list = byFile.TryGetValue(doc.Name, out var compiler) ? compiler : new List<EditorDiagnostic>();
            list.AddRange(EmbeddedAssemblyChecker.Check(doc.Editor.Text ?? string.Empty, _checker, GetAssemblyWarnings));
            doc.Diagnostics.Set(list);
        }
    }

    private void ShowDiagnosticAtCaret(EditorDocument doc)
    {
        if (doc != ActiveDocument) return;
        if (doc.Diagnostics.At(doc.Editor.TextArea.Caret.Line) is { } diagnostic)
            Status.Text = $"{diagnostic.Severity} on line {diagnostic.Line}: {FirstLine(diagnostic.Message)}";
    }

    private static void UpdateDiagnosticTip(EditorDocument doc, PointerEventArgs e)
    {
        var view = doc.Editor.TextArea.TextView;
        if (!view.VisualLinesValid) return;
        var visualLine = view.GetVisualLineFromVisualTop(e.GetPosition(view).Y + view.VerticalOffset);
        var diagnostic = visualLine is null ? null : doc.Diagnostics.At(visualLine.FirstDocumentLine.LineNumber);
        var tip = diagnostic is null ? null : $"{diagnostic.Severity}: {diagnostic.Message}";
        if (!Equals(ToolTip.GetTip(view), tip)) ToolTip.SetTip(view, tip);
    }

    private static string FirstLine(string text)
    {
        var newline = text.IndexOf('\n');
        return newline < 0 ? text : text[..newline].TrimEnd();
    }

    private static readonly Regex AsmLocation = new(@"\bLine (\d+)\b", RegexOptions.Compiled);
    private static readonly Regex CSharpLocation = new(@"^(?<file>[^()\r\n]+)\((?<line>\d+),(?<col>\d+)\)", RegexOptions.Compiled);
    private static readonly Regex StackTraceLocation = new(@" in (?<path>.+):line (?<line>\d+)", RegexOptions.Compiled);

    // Double-clicking a message jumps to its location; a line without one falls back to the first location in Messages.
    private void Messages_DoubleTapped(object? s, TappedEventArgs e)
    {
        var text = Messages.Text ?? string.Empty;
        var caret = Math.Clamp(Messages.CaretIndex, 0, text.Length);
        var start = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        var end = text.IndexOf('\n', caret);
        var line = text[start..(end < 0 ? text.Length : end)].Trim();
        if (TryNavigateToMessage(line) || TryNavigateToMessage(text)) e.Handled = true;
    }

    private bool TryNavigateToMessage(string message)
    {
        if (CSharpLocation.Match(message) is { Success: true } cs && FindOrOpenDocument(cs.Groups["file"].Value.Trim()) is { } csDoc)
            return GoToLocation(csDoc, int.Parse(cs.Groups["line"].Value), int.Parse(cs.Groups["col"].Value));
        if (StackTraceLocation.Match(message) is { Success: true } trace && FindOrOpenDocument(Path.GetFileName(trace.Groups["path"].Value)) is { } traceDoc)
            return GoToLocation(traceDoc, int.Parse(trace.Groups["line"].Value), 1);
        if (AsmLocation.Match(message) is { Success: true } asm && (_assembledDocument ?? ActiveDocument) is { } asmDoc && _documents.Contains(asmDoc))
            return GoToLocation(asmDoc, int.Parse(asm.Groups[1].Value), 1);
        return false;
    }

    private EditorDocument? FindOrOpenDocument(string fileName)
    {
        var open = _documents.FirstOrDefault(d => string.Equals(d.Name, fileName, StringComparison.OrdinalIgnoreCase));
        if (open is not null || _projectFolder is null || !Directory.Exists(_projectFolder)) return open;
        var path = EnumerateProjectFiles(_projectFolder, fileName).FirstOrDefault();
        if (path is null) return null;
        OpenFilePath(path);
        return _documents.FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    private bool GoToLocation(EditorDocument doc, int line, int column)
    {
        var index = _documents.IndexOf(doc);
        if (index < 0) return false;
        WorkspaceTabs.SelectedIndex = 0;
        EditorTabs.SelectedIndex = index;
        var editor = doc.Editor;
        var document = editor.Document;
        line = Math.Clamp(line, 1, document.LineCount);
        var docLine = document.GetLineByNumber(line);
        editor.CaretOffset = docLine.Offset + Math.Clamp(column - 1, 0, docLine.Length);
        editor.TextArea.ClearSelection();
        editor.ScrollToLine(line);
        editor.Focus();
        return true;
    }

    // ---- Edit menu ----

    private void Undo_Click(object? s, RoutedEventArgs e) => ActiveEditor?.Undo();
    private void Redo_Click(object? s, RoutedEventArgs e) => ActiveEditor?.Redo();
    private void Cut_Click(object? s, RoutedEventArgs e) => ActiveEditor?.Cut();
    private void Copy_Click(object? s, RoutedEventArgs e) => ActiveEditor?.Copy();
    private void Paste_Click(object? s, RoutedEventArgs e) => ActiveEditor?.Paste();
    private void SelectAll_Click(object? s, RoutedEventArgs e) => ActiveEditor?.SelectAll();
    private void Find_Click(object? s, RoutedEventArgs e) => OpenSearch(replace: false);
    private void Replace_Click(object? s, RoutedEventArgs e) => OpenSearch(replace: true);

    private void OpenSearch(bool replace)
    {
        var doc = ActiveDocument;
        if (doc is null) return;
        WorkspaceTabs.SelectedIndex = 0;
        var selected = doc.Editor.SelectedText;
        if (!string.IsNullOrEmpty(selected) && !selected.Contains('\n')) doc.Search.SearchPattern = selected;
        doc.Search.IsReplaceMode = replace;
        doc.Search.Open();
    }

    private async void GoToLine_Click(object? s, RoutedEventArgs e)
    {
        var doc = ActiveDocument;
        if (doc is null) return;
        var line = await GoToLineDialog.ShowAsync(this, doc.Editor.TextArea.Caret.Line, doc.Editor.Document.LineCount);
        if (line is { } target) GoToLocation(doc, target, 1);
    }

    private static readonly Regex LabelDefinition = new(@"^\s*([A-Za-z_.][A-Za-z0-9_.$]*)\s*:", RegexOptions.Compiled);

    private async void GoToLabel_Click(object? s, RoutedEventArgs e)
    {
        var doc = ActiveDocument;
        if (doc is null) return;
        if (!IsAssemblyDocument(doc)) { Status.Text = "Go to Label works in assembly files"; return; }
        var labels = doc.Editor.Document.Lines
            .Select(l => (Match: LabelDefinition.Match(doc.Editor.Document.GetText(l)), l.LineNumber))
            .Where(x => x.Match.Success)
            .Select(x => (x.Match.Groups[1].Value, x.LineNumber))
            .ToList();
        if (labels.Count == 0) { Status.Text = "No labels in this file"; return; }
        var line = await GoToLabelDialog.ShowAsync(this, labels);
        if (line is { } target) GoToLocation(doc, target, 1);
    }

    // The lines covered by the selection, or the caret line when nothing is selected.
    private static (DocumentLine First, DocumentLine Last) SelectedLines(TextEditor editor)
    {
        var document = editor.Document;
        if (editor.SelectionLength == 0)
        {
            var line = document.GetLineByOffset(editor.CaretOffset);
            return (line, line);
        }
        var first = document.GetLineByOffset(editor.SelectionStart);
        var endOffset = editor.SelectionStart + editor.SelectionLength;
        var last = document.GetLineByOffset(endOffset);
        // A selection ending at the very start of a line does not include that line.
        if (last.LineNumber > first.LineNumber && endOffset == last.Offset) last = last.PreviousLine;
        return (first, last);
    }

    private static string DelimiterOf(TextDocument document, DocumentLine line) =>
        line.DelimiterLength > 0 ? document.GetText(line.EndOffset, line.DelimiterLength)
        : line.PreviousLine is { DelimiterLength: > 0 } previous ? document.GetText(previous.EndOffset, previous.DelimiterLength)
        : "\n";

    private void ToggleComment_Click(object? s, RoutedEventArgs e)
    {
        var doc = ActiveDocument;
        if (doc is null) return;
        var editor = doc.Editor;
        var document = editor.Document;
        var token = IsAssemblyDocument(doc) ? "#" : "//";
        var (first, last) = SelectedLines(editor);
        var lines = Enumerable.Range(first.LineNumber, last.LineNumber - first.LineNumber + 1).Select(document.GetLineByNumber).ToList();
        var nonBlank = lines.Where(l => document.GetText(l).Trim().Length > 0).ToList();
        if (nonBlank.Count == 0) return;
        var uncomment = nonBlank.All(l => document.GetText(l).TrimStart().StartsWith(token, StringComparison.Ordinal));
        var indent = nonBlank.Min(l => document.GetText(l).Length - document.GetText(l).TrimStart().Length);

        document.BeginUpdate();
        try
        {
            // Work bottom-up so earlier offsets stay valid.
            foreach (var line in nonBlank.AsEnumerable().Reverse())
            {
                var text = document.GetText(line);
                if (uncomment)
                {
                    var at = text.IndexOf(token, StringComparison.Ordinal);
                    var length = token.Length + (at + token.Length < text.Length && text[at + token.Length] == ' ' ? 1 : 0);
                    document.Remove(line.Offset + at, length);
                }
                else
                {
                    document.Insert(line.Offset + indent, token + " ");
                }
            }
        }
        finally { document.EndUpdate(); }
    }

    private void DuplicateLine_Click(object? s, RoutedEventArgs e)
    {
        var editor = ActiveEditor;
        if (editor is null) return;
        var document = editor.Document;
        if (editor.SelectionLength > 0)
        {
            var end = editor.SelectionStart + editor.SelectionLength;
            document.Insert(end, editor.SelectedText);
            return;
        }
        var line = document.GetLineByOffset(editor.CaretOffset);
        var column = editor.CaretOffset - line.Offset;
        document.Insert(line.EndOffset, DelimiterOf(document, line) + document.GetText(line));
        var copy = line.NextLine!;
        editor.CaretOffset = copy.Offset + Math.Min(column, copy.Length);
    }

    private void MoveLineUp_Click(object? s, RoutedEventArgs e) => MoveLines(up: true);
    private void MoveLineDown_Click(object? s, RoutedEventArgs e) => MoveLines(up: false);

    // Swaps the selected lines (or the caret line) with the line above or below, keeping them selected.
    private void MoveLines(bool up)
    {
        var editor = ActiveEditor;
        if (editor is null) return;
        var document = editor.Document;
        var (first, last) = SelectedLines(editor);
        var block = document.GetText(first.Offset, last.EndOffset - first.Offset);
        if (up)
        {
            var previous = first.PreviousLine;
            if (previous is null) return;
            var delimiter = DelimiterOf(document, previous);
            var start = previous.Offset;
            document.Replace(start, last.EndOffset - start, block + delimiter + document.GetText(previous));
            editor.Select(start, block.Length);
        }
        else
        {
            var next = last.NextLine;
            if (next is null) return;
            var delimiter = DelimiterOf(document, last);
            var nextText = document.GetText(next);
            var start = first.Offset;
            document.Replace(start, next.EndOffset - start, nextText + delimiter + block);
            editor.Select(start + nextText.Length + delimiter.Length, block.Length);
        }
    }
    private void RefreshDisplay()
    {
        // While running, the simulator is owned by the background thread; refresh when it stops.
        if (RegistersList is null || IsRunning) return;
        var previous = PreviousRegisterValues();
        if (!ReferenceEquals(RegistersList.ItemsSource, _registerRows)) RegistersList.ItemsSource = _registerRows;
        // Row index matches the register change index: 0-31, then HI (32) and LO (33).
        for (var i = 0; i < _registerRows.Length; i++) UpdateRegisterRow(_registerRows[i], i, previous);
        PcText.Text = $"PC  0x{_machine.PC:X8}";
        SetConsoleText(_machine.ConsoleText ?? "");
        UpdateInputState();
        if (BackButton is not null) BackButton.IsEnabled = _machine.CanStepBack;
        if (BackMenuItem is not null) BackMenuItem.IsEnabled = _machine.CanStepBack;
        RefreshTextSegment(); RefreshMemoryViewer(); HighlightCurrentSourceLine();
    }

    private void UpdateRegisterRow(RegisterRow row, int index, IReadOnlyDictionary<int, int> previous)
    {
        var name = RegisterFile.NameOf(index);
        var changed = previous.TryGetValue(index, out var old);
        row.Text = $"{name,5}  {FormatRegister(_machine.Registers.GetByIndex(index))}";
        row.Background = changed ? ChangeBrush : null;
        row.Weight = changed ? FontWeight.Bold : FontWeight.Normal;
        row.Tip = changed ? $"{ChangeVerb()}; was {FormatRegister(old)}" : null;
    }

    private string ChangeVerb() => (_machine.LastStep?.IsEdit == true, _machine.LastStepWasUndo) switch
    {
        (true, true) => "Edit undone by Step Back",
        (true, false) => "Edited manually",
        (false, true) => "Restored by Step Back",
        _ => "Written by the last step"
    };

    private IBrush ChangeBrush => _machine.LastStep?.IsEdit == true ? EditedBrush : ChangedBrush;

    // Value each register held before the last step (or before the last Step Back).
    private Dictionary<int, int> PreviousRegisterValues()
    {
        var result = new Dictionary<int, int>();
        var step = _machine.LastStep;
        if (step is null) return result;
        foreach (var change in step.RegisterChanges)
        {
            if (_machine.LastStepWasUndo) result[change.Register] = change.NewValue;
            else result.TryAdd(change.Register, change.OldValue);
        }
        return result;
    }

    private Dictionary<uint, byte> PreviousMemoryBytes()
    {
        var result = new Dictionary<uint, byte>();
        var step = _machine.LastStep;
        if (step is null) return result;
        foreach (var change in step.MemoryChanges)
        {
            if (_machine.LastStepWasUndo) result[change.Address] = change.NewValue;
            else result.TryAdd(change.Address, change.OldValue);
        }
        return result;
    }
    private void RefreshTextSegment()
    {
        if (TextSegmentGrid is null) return;
        if (_program is null)
        {
            if (_textSegmentProgram is not null) { TextSegmentGrid.ItemsSource = Array.Empty<TextRow>(); _textRows = new(); _textSegmentProgram = null; }
            return;
        }
        // Rebuild rows only when a new program is assembled; replacing ItemsSource on every
        // step would reset the grid's scroll position.
        if (!ReferenceEquals(_program, _textSegmentProgram))
        {
            var sourceLines = (ActiveEditor?.Text ?? "").Replace("\r\n", "\n").Split('\n');
            _textRows = _program.Instructions.Select(x => new TextRow($"0x{x.Address:X8}", $"0x{x.MachineCode:X8}", InstructionEncoder.Describe(x.MachineCode), x.BasicSource, x.Line > 0 && x.Line <= sourceLines.Length ? $"{x.Line}: {sourceLines[x.Line - 1].Trim()}" : $"Line {x.Line}")).ToList();
            TextSegmentGrid.ItemsSource = _textRows;
            _textSegmentProgram = _program;
        }
        var index = !_machine.Halted && _machine.InstructionIndex < _textRows.Count ? _machine.InstructionIndex : -1;
        if (TextSegmentGrid.SelectedIndex != index) TextSegmentGrid.SelectedIndex = index;
        if (index >= 0) TextSegmentGrid.ScrollIntoView(_textRows[index], null);
    }
    private void HighlightCurrentSourceLine()
    {
        var editor = ActiveEditor;
        if (editor is null || _program is null || _machine.Halted ||
            _machine.InstructionIndex >= _program.Instructions.Count || editor.Document is null)
            return;

        var lineNo = _program.Instructions[_machine.InstructionIndex].Line;
        if (lineNo < 1 || lineNo > editor.Document.LineCount)
            return;

        var line = editor.Document.GetLineByNumber(lineNo);
        editor.Select(line.Offset, 0);
        editor.TextArea.Caret.Offset = line.Offset;
        editor.ScrollToLine(lineNo);
    }
    private void MemorySegment_SelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        if (MemorySegment is null || MemoryAddressBox is null) return;
        _memoryViewStart = MemorySegment.SelectedIndex switch
        {
            DataSegment => MipsMemory.DataBase,
            HeapSegment => MipsMemory.HeapBase,
            _ => FollowedRegisterStart() ?? _memoryViewStart
        };
        MemoryAddressBox.Text = $"0x{_memoryViewStart:X8}";
        RefreshMemoryViewer();
    }

    private void MemoryGo_Click(object? s, RoutedEventArgs e)
    {
        Try(() =>
        {
            var text = (MemoryAddressBox?.Text ?? string.Empty).Trim();
            if (_program is not null && _program.Symbols.TryGetValue(text, out var symbolAddress))
                _memoryViewStart = symbolAddress & 0xfffffff0u;
            else if (text.StartsWith('$'))
                _memoryViewStart = unchecked((uint)_machine.Registers[RegisterFile.Parse(text)]) & 0xfffffff0u;
            else if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                _memoryViewStart = Convert.ToUInt32(text[2..], 16) & 0xfffffff0u;
            else
                _memoryViewStart = Convert.ToUInt32(text) & 0xfffffff0u;

            if (MemorySegment is not null) MemorySegment.SelectedIndex = CustomSegment;
            if (MemoryAddressBox is not null) MemoryAddressBox.Text = $"0x{_memoryViewStart:X8}";
            RefreshMemoryViewer();
        });
    }

    // Start address for the Stack / $gp / $fp views, or null for the other views.
    private uint? FollowedRegisterStart() => MemorySegment?.SelectedIndex switch
    {
        StackSegment => RegisterWindowStart(29, 0x80),
        GpSegment => RegisterWindowStart(28, 0x40),
        FpSegment => RegisterWindowStart(30, 0x40),
        _ => null
    };

    // A window starting a few rows before the address in the register.
    private uint RegisterWindowStart(int register, uint bytesBefore)
    {
        var value = unchecked((uint)_machine.Registers[register]);
        if (value == 0) value = MipsMemory.StackTop;
        var aligned = value & 0xfffffff0u;
        return aligned >= bytesBefore ? aligned - bytesBefore : 0u;
    }

    private void RefreshMemoryViewer()
    {
        if (MemoryGrid is null || IsRunning) return;
        if (FollowedRegisterStart() is { } follow && follow != _memoryViewStart)
        {
            _memoryViewStart = follow;
            if (MemoryAddressBox is not null) MemoryAddressBox.Text = $"0x{follow:X8}";
        }
        var previous = PreviousMemoryBytes();
        if (!ReferenceEquals(MemoryGrid.ItemsSource, _memoryRows)) MemoryGrid.ItemsSource = _memoryRows;
        var start = _memoryViewStart & 0xfffffff0u;
        for (var row = 0; row < _memoryRows.Length; row++)
        {
            var address = unchecked(start + (uint)(row * 16));
            var ascii = new char[16];
            for (var i = 0; i < 16; i++)
            {
                var b = _machine.Memory.ReadByte(address + (uint)i);
                ascii[i] = b is >= 32 and <= 126 ? (char)b : '.';
            }

            var r = _memoryRows[row];
            r.Address = $"0x{address:X8}";
            r.W0 = MemoryWordCell(address, previous);
            r.W4 = MemoryWordCell(address + 4, previous);
            r.W8 = MemoryWordCell(address + 8, previous);
            r.WC = MemoryWordCell(address + 12, previous);
            r.Ascii = new string(ascii);
        }
    }

    private MemoryCell MemoryWordCell(uint address, IReadOnlyDictionary<uint, byte> previous)
    {
        var text = FormatMemoryWord(_machine.Memory.ReadWordUnsigned(address));
        var changed = false;
        uint old = 0;
        for (var i = 0; i < 4; i++)
        {
            var byteAddress = unchecked(address + (uint)i);
            if (previous.TryGetValue(byteAddress, out var oldByte)) changed = true;
            else oldByte = _machine.Memory.ReadByte(byteAddress);
            old |= (uint)oldByte << (i * 8);
        }
        var rowStart = address & 0xfffffff0u;
        return new MemoryCell(text,
            changed ? ChangeBrush : null,
            changed ? FontWeight.Bold : FontWeight.Normal,
            changed ? $"{ChangeVerb()}; was {FormatMemoryWord(old)}" : null,
            address,
            new RelayCommand(() => EditMemoryWord(address)),
            new RelayCommand(() => CopyText(FormatMemoryWord(_machine.Memory.ReadWordUnsigned(address)))),
            new RelayCommand(() => CopyText($"0x{address:X8}")),
            new RelayCommand(() => CopyText(MemoryRowText(rowStart))),
            new RelayCommand(() => CopyText(MemoryAscii(rowStart))),
            new RelayCommand(() => ShowInMemory(_machine.Memory.ReadWordUnsigned(address))));
    }

    private string FormatMemoryWord(uint value)
    {
        var mode = MemoryFormat?.SelectedIndex ?? 0;
        return mode switch
        {
            1 => unchecked((int)value).ToString(),
            2 => value.ToString(),
            3 => Convert.ToString(value, 2).PadLeft(32, '0'),
            4 => FormatWordAscii(value),
            _ => $"0x{value:X8}"
        };
    }

    private static string FormatWordAscii(uint value)
    {
        Span<char> chars = stackalloc char[4];
        for (var i = 0; i < 4; i++)
        {
            var b = (byte)(value >> (i * 8));
            chars[i] = b is >= 32 and <= 126 ? (char)b : '.';
        }
        return new string(chars);
    }

    private string FormatRegister(int value) { var mode = RegisterFormat?.SelectedIndex ?? 0; var u = unchecked((uint)value); return mode switch { 1 => value.ToString(), 2 => u.ToString(), 3 => Convert.ToString(u, 2).PadLeft(32, '0'), 4 => FormatAscii(u), _ => $"0x{u:X8}" }; }
    private static string FormatAscii(uint value) { var b = (byte)(value & 0xFF); return b switch { 0 => "'\\0'", 9 => "'\\t'", 10 => "'\\n'", 13 => "'\\r'", >= 32 and <= 126 => $"'{(char)b}'", _ => $"'\\x{b:X2}'" }; }
    // CodeDetails is the field-by-field breakdown of the machine word, shown when hovering the Code cell.
    private sealed record TextRow(string Address, string Code, string CodeDetails, string Basic, string Source);
    private sealed record MemoryCell(string Text, IBrush? Background, FontWeight Weight, string? Tip, uint Address = 0,
        System.Windows.Input.ICommand? EditCommand = null, System.Windows.Input.ICommand? CopyValueCommand = null,
        System.Windows.Input.ICommand? CopyAddressCommand = null, System.Windows.Input.ICommand? CopyRowCommand = null,
        System.Windows.Input.ICommand? CopyAsciiCommand = null, System.Windows.Input.ICommand? FollowPointerCommand = null);

    // Register and memory rows are created once and updated in place (with change notifications)
    // so the lists keep their scroll position and selection while stepping.
    private abstract class BindableRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private sealed class RegisterRow : BindableRow
    {
        public RegisterRow(int index) => Index = index;
        public int Index { get; }
        public System.Windows.Input.ICommand? EditCommand { get; set; }
        public System.Windows.Input.ICommand? CopyNameCommand { get; set; }
        public System.Windows.Input.ICommand? CopyValueCommand { get; set; }
        public System.Windows.Input.ICommand? ShowInMemoryCommand { get; set; }
        private string _text = "";
        private IBrush? _background;
        private FontWeight _weight = FontWeight.Normal;
        private string? _tip;
        public string Text { get => _text; set => Set(ref _text, value); }
        public IBrush? Background { get => _background; set => Set(ref _background, value); }
        public FontWeight Weight { get => _weight; set => Set(ref _weight, value); }
        public string? Tip { get => _tip; set => Set(ref _tip, value); }
    }

    private sealed class MemoryRow : BindableRow
    {
        private static readonly MemoryCell Empty = new("", null, FontWeight.Normal, null);
        private string _address = "";
        private MemoryCell _w0 = Empty, _w4 = Empty, _w8 = Empty, _wc = Empty;
        private string _ascii = "";
        public string Address { get => _address; set => Set(ref _address, value); }
        public MemoryCell W0 { get => _w0; set => Set(ref _w0, value); }
        public MemoryCell W4 { get => _w4; set => Set(ref _w4, value); }
        public MemoryCell W8 { get => _w8; set => Set(ref _w8, value); }
        public MemoryCell WC { get => _wc; set => Set(ref _wc, value); }
        public string Ascii { get => _ascii; set => Set(ref _ascii, value); }
    }
    private sealed class EditorDocument
    {
        public string? Path;
        public string Name;
        public TextEditor Editor;
        public Border EditorBorder;
        public BreakpointMargin Breakpoints;
        public DiagnosticRenderer Diagnostics;
        public SearchPanel Search;
        public bool Dirty;

        public EditorDocument(string? path, string name, TextEditor editor, Border editorBorder, BreakpointMargin breakpoints,
            DiagnosticRenderer diagnostics, SearchPanel search, bool dirty)
        {
            Path = path;
            Name = name;
            Editor = editor;
            EditorBorder = editorBorder;
            Breakpoints = breakpoints;
            Diagnostics = diagnostics;
            Search = search;
            Dirty = dirty;
        }
    }
}

internal sealed class AppSettings
{
    public bool ShowLineNumbers { get; set; } = true;
    public bool SyntaxHighlighting { get; set; } = true;
    public bool CompileAllCSharpFiles { get; set; }

    // Display format index for each viewer: 0 Hex, 1 Signed, 2 Unsigned, 3 Binary, 4 ASCII.
    public int RegisterFormatIndex { get; set; }
    public int MemoryFormatIndex { get; set; }

    // Session state, restored on the next launch.
    public string? ProjectFolder { get; set; }
    public List<string> OpenFiles { get; set; } = new();
    public string? ActiveFile { get; set; }
    internal static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ASMForge", "settings.json");
    public static AppSettings Load() { try { return File.Exists(SettingsPath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new() : new(); } catch { return new(); } }
    public void Save() { try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); } catch { } }
}

internal sealed class CodeColorizer : DocumentColorizingTransformer
{
    private readonly Func<string> _name;
    private readonly Func<ThemeVariant?> _theme;

    private static readonly string[] TokenGroupNames =
    {
        "comment", "string", "directive", "register", "number", "label", "keyword", "cs"
    };

    private static readonly Regex TokenRegex = new(
        "(?<comment>\\#.*$|//.*$)|" +
        "(?<string>\\\"(?:\\\\.|[^\\\"\\\\])*\\\")|" +
        "(?<directive>\\.[A-Za-z_][\\w.]*)|" +
        "(?<register>\\$(?:zero|at|v[01]|a[0-3]|t[0-9]|s[0-7]|k[01]|gp|sp|fp|ra|\\d+))|" +
        "(?<number>\\b(?:0x[0-9A-Fa-f]+|\\d+)\\b)|" +
        "(?<label>\\b[A-Za-z_]\\w*(?=:))|" +
        "(?<keyword>\\b(?:add|addu|addi|addiu|sub|subu|mul|mult|multu|div|divu|rem|and|andi|or|ori|xor|xori|nor|sll|srl|sra|slt|slti|sltu|sltiu|lw|sw|lb|lbu|lh|lhu|sb|sh|li|la|move|mfhi|mflo|mthi|mtlo|beq|bne|bgt|bge|blt|ble|j|jal|jr|syscall|nop)\\b)|" +
        "(?<cs>\\b(?:using|namespace|class|struct|public|private|internal|protected|static|void|int|uint|string|bool|const|return|new|if|else|for|foreach|while|switch|case|break|true|false|null|var)\\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public CodeColorizer(Func<string> name, Func<ThemeVariant?> theme)
    {
        _name = name;
        _theme = theme;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        var isCs = _name().EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
        var dark = _theme() == ThemeVariant.Dark;

        foreach (Match match in TokenRegex.Matches(text))
        {
            Group? token = null;
            string? tokenType = null;

            foreach (var groupName in TokenGroupNames)
            {
                var group = match.Groups[groupName];
                if (!group.Success)
                    continue;

                token = group;
                tokenType = groupName;
                break;
            }

            if (token is null || tokenType is null)
                continue;

            if (isCs && tokenType is "keyword" or "directive" or "register" or "label")
                continue;

            if (!isCs && tokenType == "cs")
                continue;

            var brush = tokenType switch
            {
                "comment" => new SolidColorBrush(dark ? Color.FromRgb(106, 153, 85) : Color.FromRgb(0, 128, 0)),
                "string" => new SolidColorBrush(dark ? Color.FromRgb(206, 145, 120) : Color.FromRgb(163, 21, 21)),
                "directive" => new SolidColorBrush(dark ? Color.FromRgb(197, 134, 192) : Color.FromRgb(128, 0, 128)),
                "register" => new SolidColorBrush(dark ? Color.FromRgb(78, 201, 176) : Color.FromRgb(0, 128, 128)),
                "number" => new SolidColorBrush(dark ? Color.FromRgb(181, 206, 168) : Color.FromRgb(9, 134, 88)),
                "label" => new SolidColorBrush(dark ? Color.FromRgb(220, 220, 170) : Color.FromRgb(121, 94, 38)),
                _ => new SolidColorBrush(dark ? Color.FromRgb(86, 156, 214) : Color.FromRgb(0, 0, 255))
            };

            ChangeLinePart(
                line.Offset + token.Index,
                line.Offset + token.Index + token.Length,
                element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}
