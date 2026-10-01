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
using ASMForge.Core.Assembly;
using ASMForge.Core.Cpu;
using ASMForge.Core.Execution;
using ASMForge.Core.Memory;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using ASMForge.App;

namespace ASMForge.App.Views;

public partial class MainWindow : Window
{
    private readonly SimpleAssembler _assembler = new();
    private readonly MipsMachine _machine = new();
    private AssemblyProgram? _program;
    private readonly List<EditorDocument> _documents = new();
    // Semi-transparent amber reads on both light and dark themes; changed values are also bolded.
    private static readonly IBrush ChangedBrush = new ImmutableSolidColorBrush(Color.FromArgb(110, 255, 196, 0));
    private readonly RegisterRow[] _registerRows = Enumerable.Range(0, RegisterFile.Count + 2).Select(_ => new RegisterRow()).ToArray();
    private readonly MemoryRow[] _memoryRows = Enumerable.Range(0, 16).Select(_ => new MemoryRow()).ToArray();
    private AssemblyProgram? _textSegmentProgram;
    private List<TextRow> _textRows = new();
    private CompletionWindow? _completionWindow;
    private string? _projectFolder;
    private AppSettings _settings = AppSettings.Load();
    // True while restoring or tearing down, so tab changes don't overwrite the saved session.
    private bool _sessionPaused;
    private const string Sample = "# ASMForge v0.8.1 sample\nli $t0, 10\nli $t1, 3\nrem $t2, $t0, $t1\n\nmove $a0, $t2\nli $v0, 1\nsyscall\nli $v0, 10\nsyscall\n";
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
    private uint _memoryViewStart = MipsMemory.DataBase;

    public MainWindow()
    {
        DiagnosticLog.Info("MainWindow constructor started");
        InitializeComponent();
        DiagnosticLog.Info("MainWindow XAML initialized");
        ShowLineNumbersMenu.IsChecked = _settings.ShowLineNumbers;
        SyntaxHighlightingMenu.IsChecked = _settings.SyntaxHighlighting;
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
                if (!File.Exists(path)) File.WriteAllText(path, r.Kind == 1 ? CsTemplate : "# ASMForge assembly file\n.text\nmain:\n");
                OpenFilePath(path);
                RefreshExplorer();
                Status.Text = $"Created {name}";
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void Open_Click(object? s, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open source file", AllowMultiple = true, FileTypeFilter = new[] { new FilePickerFileType("Assembly files (*.asm, *.s)") { Patterns = new[] { "*.asm", "*.s" } }, new FilePickerFileType("C# files (*.cs)") { Patterns = new[] { "*.cs" } }, new FilePickerFileType("All ASMForge source files") { Patterns = new[] { "*.asm", "*.s", "*.cs" } }, FilePickerFileTypes.All } });
        foreach (var f in files) OpenFilePath(f.Path.LocalPath);
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
        var doc = ActiveDocument; if (doc is null) return; var path = doc.Path;
        if (saveAs || path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save file", SuggestedFileName = doc.Name });
            if (file is null) return; path = file.Path.LocalPath;
        }
        try { File.WriteAllText(path!, doc.Editor.Text ?? ""); doc.Path = path; doc.Name = Path.GetFileName(path); doc.Dirty = false; UpdateTabHeaders(); RefreshExplorer(); SaveSession(); Status.Text = $"Saved {doc.Name}"; }
        catch (Exception ex) { ShowError(ex); }
    }
    private void CloseFile_Click(object? s, RoutedEventArgs e) => CloseDocumentAt(EditorTabs.SelectedIndex);

    private void CloseDocumentAt(int index)
    {
        if (index < 0 || index >= _documents.Count) return;
        // Remove AvaloniaEdit's line-number margin before detaching the editor.
        // This avoids the known teardown path where the margin can render with an invalid font size.
        _documents[index].Editor.ShowLineNumbers = false;
        _documents.RemoveAt(index);
        EditorTabs.Items.RemoveAt(index);
        if (_documents.Count == 0)
            OpenDocument(null, "Untitled.asm", "", true);
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

        var editorBorder = new Border
        {
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(5),
            Margin = new Avalonia.Thickness(2, 0, 2, 2),
            Child = editor
        };

        ApplyEditorTheme(editor, editorBorder);

        DiagnosticLog.Info($"Editor created: {name}; syntax={_settings.SyntaxHighlighting}; transformers={editor.TextArea.TextView.LineTransformers.Count}; theme={RequestedThemeVariant}");

        var doc = new EditorDocument(path, name, editor, editorBorder, dirty);
        editor.TextChanged += (_, _) =>
        {
            doc.Dirty = true;
            UpdateTabHeaders();
            _program = null;
        };

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
            CloseDocumentAt(index);
            e.Handled = true;
        };

        EditorTabs.Items.Add(tab);
        EditorTabs.SelectedIndex = _documents.Count - 1;
        UpdateTabHeaders();
        editor.Focus();
        SaveSession();

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
    private void UpdateTabHeaders() { for (var i = 0; i < _documents.Count && i < EditorTabs.Items.Count; i++) if (EditorTabs.Items[i] is TabItem t) t.Header = _documents[i].Name + (_documents[i].Dirty ? " *" : ""); }
    private void RefreshExplorer()
    {
        if (ExplorerList is null || ProjectNameText is null)
            return;

        var items = new List<string>();
        if (_projectFolder is not null && Directory.Exists(_projectFolder))
        {
            ProjectNameText.Text = GetProjectDisplayName(_projectFolder);
            foreach (var file in Directory.EnumerateFiles(_projectFolder, "*", SearchOption.AllDirectories)
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

    private void ExplorerList_DoubleTapped(object? s, TappedEventArgs e) { if (_projectFolder is null || ExplorerList.SelectedItem is not string item) return; var rel = item.Trim(); if (rel == "No project/folder open") return; var path = Path.Combine(_projectFolder, rel); if (File.Exists(path)) OpenFilePath(path); }
    private void EditorTabs_SelectionChanged(object? s, SelectionChangedEventArgs e) { _program = null; SaveSession(); Status.Text = ActiveDocument is null ? "Ready" : ActiveDocument.Name; ActiveEditor?.Focus(); DiagnosticLog.Info($"Editor tab changed: index={EditorTabs.SelectedIndex}, active={ActiveDocument?.Name ?? "<none>"}"); Avalonia.Threading.Dispatcher.UIThread.Post(() => LogEditorDiagnostics("tab-selection-changed"), Avalonia.Threading.DispatcherPriority.Loaded); }
    private void ShowLineNumbers_Click(object? s, RoutedEventArgs e) { _settings.ShowLineNumbers = ShowLineNumbersMenu.IsChecked; foreach (var d in _documents) d.Editor.ShowLineNumbers = _settings.ShowLineNumbers; _settings.Save(); }
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
        _program = _assembler.Assemble(source);
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
        if (ActiveDocument?.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
        {
            Status.Text = "C# files are compiled when you press Run";
            Messages.Text = "C# source detected. Press Run (F5) to compile all C# files in the current project and execute its Main method.";
            OutputTabs.SelectedIndex = 0;
            return;
        }
        Try(Assemble);
    }

    private void Step_Click(object? s, RoutedEventArgs e)
    {
        if (ActiveDocument?.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
        {
            Messages.Text = "Instruction stepping applies to MIPS assembly. Run the C# host, then use ASMForgeRuntime.Step() inside C# when host-controlled stepping is needed.";
            OutputTabs.SelectedIndex = 0;
            Status.Text = "C# host stepping is API-controlled";
            return;
        }
        Try(() => { if (_program is null) Assemble(); _machine.Step(); Status.Text = _machine.Halted ? "Finished" : "Stepped"; RefreshDisplay(); });
    }

    private async void Run_Click(object? s, RoutedEventArgs e)
    {
        if (ActiveDocument?.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
        {
            await RunCSharpAsync();
            return;
        }
        Try(() => { if (_program is null) Assemble(); _machine.Run(); Status.Text = "Finished"; WorkspaceTabs.SelectedIndex = 1; RefreshDisplay(); });
    }

    private void Reset_Click(object? s, RoutedEventArgs e)
    {
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

            var result = await CSharpRunner.CompileAndRunAsync(sources);
            Console.Text = result.Output;
            Messages.Text = result.Diagnostics;
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

        // A C# file inside an open project compiles with all C# project files.
        // A loose C# file compiles by itself, even if another project is open.
        if (activeIsProjectMember && _projectFolder is not null && Directory.Exists(_projectFolder))
        {
            foreach (var path in Directory.EnumerateFiles(_projectFolder, "*.cs", SearchOption.AllDirectories)
                         .Where(p => !IsBuildOutputPath(p)))
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

    private static bool IsBuildOutputPath(string path)
    {
        var parts = Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase) || p.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private void RegisterFormat_SelectionChanged(object? s, SelectionChangedEventArgs e) { if (RegistersList is not null) RefreshDisplay(); }
    private void MemoryFormat_SelectionChanged(object? s, SelectionChangedEventArgs e) { if (MemoryGrid is not null) RefreshMemoryViewer(); }
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
    private void Window_KeyDown(object? s, KeyEventArgs e) { if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.N) { New_Click(s, new RoutedEventArgs()); e.Handled = true; } else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.O) { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) OpenFolder_Click(s, new RoutedEventArgs()); else Open_Click(s, new RoutedEventArgs()); e.Handled = true; } else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S) { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) SaveAs_Click(s, new RoutedEventArgs()); else Save_Click(s, new RoutedEventArgs()); e.Handled = true; } else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.W) { CloseFile_Click(s, new RoutedEventArgs()); e.Handled = true; } else if (e.Key == Key.F3) { Assemble_Click(s, new RoutedEventArgs()); e.Handled = true; } else if (e.Key == Key.F5) { Run_Click(s, new RoutedEventArgs()); e.Handled = true; } else if (e.Key == Key.F9) { Back_Click(s, new RoutedEventArgs()); e.Handled = true; } else if (e.Key == Key.F10) { Step_Click(s, new RoutedEventArgs()); e.Handled = true; } }
    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        // AvaloniaEdit 11.x can try to render its line-number margin while the
        // window is being torn down. At that point inherited font metrics may
        // already be invalid, which can produce an emSize <= 0 exception.
        // Remove the line-number margins before the compositor disposes them.
        try
        {
            // Save the session first, then pause saving so teardown tab changes can't overwrite it.
            SaveSession();
            _sessionPaused = true;
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
    private void ShowError(Exception ex) { DiagnosticLog.Error("UI operation failed", ex); Status.Text = "Error"; Messages.Text = ex.Message; OutputTabs.SelectedIndex = 0; RefreshDisplay(); }
    private void RefreshDisplay()
    {
        if (RegistersList is null) return;
        var previous = PreviousRegisterValues();
        if (!ReferenceEquals(RegistersList.ItemsSource, _registerRows)) RegistersList.ItemsSource = _registerRows;
        // Row index matches the register change index: 0-31, then HI (32) and LO (33).
        for (var i = 0; i < _registerRows.Length; i++) UpdateRegisterRow(_registerRows[i], i, previous);
        PcText.Text = $"PC  0x{_machine.PC:X8}";
        Console.Text = _machine.ConsoleText ?? "";
        if (BackButton is not null) BackButton.IsEnabled = _machine.CanStepBack;
        if (BackMenuItem is not null) BackMenuItem.IsEnabled = _machine.CanStepBack;
        RefreshTextSegment(); RefreshMemoryViewer(); HighlightCurrentSourceLine();
    }

    private void UpdateRegisterRow(RegisterRow row, int index, IReadOnlyDictionary<int, int> previous)
    {
        var name = RegisterFile.NameOf(index);
        var changed = previous.TryGetValue(index, out var old);
        row.Text = $"{name,5}  {FormatRegister(_machine.Registers.GetByIndex(index))}";
        row.Background = changed ? ChangedBrush : null;
        row.Weight = changed ? FontWeight.Bold : FontWeight.Normal;
        row.Tip = changed ? $"{ChangeVerb()}; was {FormatRegister(old)}" : null;
    }

    private string ChangeVerb() => _machine.LastStepWasUndo ? "Restored by Step Back" : "Written by the last step";

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
            _textRows = _program.Instructions.Select(x => new TextRow($"0x{x.Address:X8}", "—", x.BasicSource, x.Line > 0 && x.Line <= sourceLines.Length ? $"{x.Line}: {sourceLines[x.Line - 1].Trim()}" : $"Line {x.Line}")).ToList();
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
            0 => MipsMemory.DataBase,
            1 => MipsMemory.HeapBase,
            2 => StackWindowStart(),
            _ => _memoryViewStart
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
            else if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                _memoryViewStart = Convert.ToUInt32(text[2..], 16) & 0xfffffff0u;
            else
                _memoryViewStart = Convert.ToUInt32(text) & 0xfffffff0u;

            if (MemorySegment is not null) MemorySegment.SelectedIndex = 3;
            if (MemoryAddressBox is not null) MemoryAddressBox.Text = $"0x{_memoryViewStart:X8}";
            RefreshMemoryViewer();
        });
    }

    private uint StackWindowStart()
    {
        var sp = unchecked((uint)_machine.Registers[29]);
        if (sp == 0) sp = MipsMemory.StackTop;
        var aligned = sp & 0xfffffff0u;
        return aligned >= 0x80 ? aligned - 0x80u : 0u;
    }

    private void RefreshMemoryViewer()
    {
        if (MemoryGrid is null) return;
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
        return changed
            ? new MemoryCell(text, ChangedBrush, FontWeight.Bold, $"{ChangeVerb()}; was {FormatMemoryWord(old)}")
            : new MemoryCell(text, null, FontWeight.Normal, null);
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
    private sealed record TextRow(string Address, string Code, string Basic, string Source);
    private sealed record MemoryCell(string Text, IBrush? Background, FontWeight Weight, string? Tip);

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
        public bool Dirty;

        public EditorDocument(string? path, string name, TextEditor editor, Border editorBorder, bool dirty)
        {
            Path = path;
            Name = name;
            Editor = editor;
            EditorBorder = editorBorder;
            Dirty = dirty;
        }
    }
}

internal sealed class AppSettings
{
    public bool ShowLineNumbers { get; set; } = true;
    public bool SyntaxHighlighting { get; set; } = true;

    // Session state, restored on the next launch.
    public string? ProjectFolder { get; set; }
    public List<string> OpenFiles { get; set; } = new();
    public string? ActiveFile { get; set; }
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ASMForge", "settings.json");
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
