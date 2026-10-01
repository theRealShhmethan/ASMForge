using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using ASMForge.Core.Assembly;
using ASMForge.Core.Cpu;
using ASMForge.Core.Execution;
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
    private readonly Stack<int> _history = new();
    private readonly List<EditorDocument> _documents = new();
    private string? _projectFolder;
    private AppSettings _settings = AppSettings.Load();
    private const string Sample = "# ASMForge v0.6.5 sample\nli $t0, 10\nli $t1, 3\nrem $t2, $t0, $t1\n\nmove $a0, $t2\nli $v0, 1\nsyscall\nli $v0, 10\nsyscall\n";
    private const string CsTemplate = """using System;\n\nnamespace ASMForgeProject;\n\ninternal static class Program\n{\n    // Assembly source is kept beside the C# code so ASMForge can route it\n    // through its simulated MIPS engine. Native host memory/registers are never touched.\n    private const string AssemblySource = \"\"\"\n.text\nmain:\n    li $t0, 5\n    li $t1, 6\n    add $t2, $t0, $t1\n    li $v0, 10\n    syscall\n\"\"\";\n\n    private static void Main()\n    {\n        // v0.6 prepares the interop template. The C# -> simulated MIPS runtime bridge\n        // will connect this source to ASMForge.Core in the next runtime layer.\n        Console.WriteLine(\"ASMForge C# + ASM project ready.\");\n    }\n}\n""";

    public MainWindow()
    {
        DiagnosticLog.Info("MainWindow constructor started");
        InitializeComponent();
        DiagnosticLog.Info("MainWindow XAML initialized");
        ShowLineNumbersMenu.IsChecked = _settings.ShowLineNumbers;
        OpenDocument(null, "main.asm", Sample, false);
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
            DiagnosticLog.Info($"ActiveDocument={doc.Name}; TextLength={ed.Text?.Length ?? 0}; EditorBounds={ed.Bounds.Width:0.##}x{ed.Bounds.Height:0.##}; IsVisible={ed.IsVisible}; IsEffectivelyVisible={ed.IsEffectivelyVisible}; Parent={ed.Parent?.GetType().FullName ?? "<null>"}; LineNumbers={ed.ShowLineNumbers}");
        }
        catch (Exception ex) { DiagnosticLog.Error("Failed to collect editor diagnostics", ex); }
    }

    private EditorDocument? ActiveDocument => EditorTabs.SelectedIndex >= 0 && EditorTabs.SelectedIndex < _documents.Count ? _documents[EditorTabs.SelectedIndex] : null;
    private TextEditor? ActiveEditor => ActiveDocument?.Editor;

    private async void New_Click(object? s, RoutedEventArgs e)
    {
        var dialog = new NewItemDialog(); var ok = await dialog.ShowDialog<bool>(this);
        if (!ok || dialog.Result is null) return;
        try
        {
            var r = dialog.Result;
            if (r.Kind == 2)
            {
                var folder = Path.Combine(r.Location, r.Name); Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, r.Name + ".asmforge"), $"{{\n  \"name\": \"{r.Name.Replace("\"", "")}\",\n  \"version\": 1\n}}\n");
                var main = Path.Combine(folder, "main.asm"); if (!File.Exists(main)) File.WriteAllText(main, "# " + r.Name + "\n.text\nmain:\n    li $v0, 10\n    syscall\n");
                _projectFolder = folder; CloseAllDocuments(); OpenFilePath(main); RefreshExplorer(); Status.Text = $"Created project {r.Name}";
            }
            else
            {
                var ext = r.Kind == 1 ? ".cs" : ".asm"; var name = r.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? r.Name : r.Name + ext;
                Directory.CreateDirectory(r.Location); var path = Path.Combine(r.Location, name);
                if (!File.Exists(path)) File.WriteAllText(path, r.Kind == 1 ? CsTemplate : "# ASMForge assembly file\n.text\nmain:\n");
                OpenFilePath(path); Status.Text = $"Created {name}";
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
        if (folders.Count == 0) return; _projectFolder = folders[0].Path.LocalPath; RefreshExplorer(); Status.Text = $"Opened {Path.GetFileName(_projectFolder)}";
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
        try { File.WriteAllText(path!, doc.Editor.Text ?? ""); doc.Path = path; doc.Name = Path.GetFileName(path); doc.Dirty = false; UpdateTabHeaders(); RefreshExplorer(); Status.Text = $"Saved {doc.Name}"; }
        catch (Exception ex) { ShowError(ex); }
    }
    private void CloseFile_Click(object? s, RoutedEventArgs e) { var i = EditorTabs.SelectedIndex; if (i < 0) return; _documents.RemoveAt(i); EditorTabs.Items.RemoveAt(i); if (_documents.Count == 0) OpenDocument(null, "Untitled.asm", "", true); else { EditorTabs.SelectedIndex = Math.Min(i, _documents.Count - 1); } }

    private void OpenFilePath(string path)
    {
        var existing = _documents.FindIndex(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)); if (existing >= 0) { EditorTabs.SelectedIndex = existing; return; }
        OpenDocument(path, Path.GetFileName(path), File.ReadAllText(path), false);
    }
    private void OpenDocument(string? path, string name, string text, bool dirty)
    {
        DiagnosticLog.Info($"Opening document: {name}; path={path ?? "<untitled>"}; chars={text.Length}");
        var editor = new TextEditor { Text = text, ShowLineNumbers = _settings.ShowLineNumbers, FontFamily = new FontFamily("Cascadia Mono,Consolas"), FontSize = 15, WordWrap = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        editor.Options.ConvertTabsToSpaces = true; editor.Options.IndentationSize = 4;
        editor.TextArea.TextView.LineTransformers.Add(new CodeColorizer(() => name, () => RequestedThemeVariant));
        editor.TextArea.KeyDown += (_, e) => HandleEditorIndent(editor, e);
        var doc = new EditorDocument(path, name, editor, dirty); editor.TextChanged += (_, _) => { doc.Dirty = true; UpdateTabHeaders(); _program = null; };
        _documents.Add(doc); var tab = new TabItem { Header = name, Content = editor, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch }; EditorTabs.Items.Add(tab); EditorTabs.SelectedIndex = _documents.Count - 1; UpdateTabHeaders(); editor.Focus(); DiagnosticLog.Info($"Editor attached to tab: {name}; tabs={EditorTabs.Items.Count}; editorParent={editor.Parent?.GetType().Name ?? "<null>"}"); Avalonia.Threading.Dispatcher.UIThread.Post(() => LogEditorDiagnostics($"opened-{name}"), Avalonia.Threading.DispatcherPriority.Loaded);
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
        if (ExplorerList is null) return; var items = new List<string>();
        if (_projectFolder is not null && Directory.Exists(_projectFolder))
        {
            items.Add("▼ " + Path.GetFileName(_projectFolder));
            foreach (var f in Directory.EnumerateFiles(_projectFolder, "*", SearchOption.AllDirectories).Where(p => p.EndsWith(".asm", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".s", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))) items.Add("   " + Path.GetRelativePath(_projectFolder, f));
        }
        else items.Add("No project/folder open"); ExplorerList.ItemsSource = items;
    }
    private void ExplorerList_DoubleTapped(object? s, TappedEventArgs e) { if (_projectFolder is null || ExplorerList.SelectedItem is not string item) return; var rel = item.Trim(); if (rel.StartsWith("▼") || rel == "No project/folder open") return; var path = Path.Combine(_projectFolder, rel); if (File.Exists(path)) OpenFilePath(path); }
    private void EditorTabs_SelectionChanged(object? s, SelectionChangedEventArgs e) { _program = null; Status.Text = ActiveDocument is null ? "Ready" : ActiveDocument.Name; ActiveEditor?.Focus(); DiagnosticLog.Info($"Editor tab changed: index={EditorTabs.SelectedIndex}, active={ActiveDocument?.Name ?? "<none>"}"); Avalonia.Threading.Dispatcher.UIThread.Post(() => LogEditorDiagnostics("tab-selection-changed"), Avalonia.Threading.DispatcherPriority.Loaded); }
    private void ShowLineNumbers_Click(object? s, RoutedEventArgs e) { _settings.ShowLineNumbers = ShowLineNumbersMenu.IsChecked; foreach (var d in _documents) d.Editor.ShowLineNumbers = _settings.ShowLineNumbers; _settings.Save(); }

    private void Assemble() { var editor = ActiveEditor ?? throw new InvalidOperationException("No file is open."); if (ActiveDocument?.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true) throw new InvalidOperationException("C# runtime execution is not implemented yet. The v0.6 C# template is prepared for the upcoming ASMForge simulated-MIPS bridge."); _program = _assembler.Assemble(editor.Text ?? ""); _machine.Load(_program); _history.Clear(); Status.Text = $"Assembled {_program.Instructions.Count} basic instruction(s)"; Messages.Text = $"Assemble completed successfully.\n{_program.Instructions.Count} basic instruction(s) generated."; Console.Text = ""; OutputTabs.SelectedIndex = 1; WorkspaceTabs.SelectedIndex = 1; RefreshDisplay(); }
    private void Assemble_Click(object? s, RoutedEventArgs e) => Try(Assemble);
    private void Step_Click(object? s, RoutedEventArgs e) => Try(() => { if (_program is null) Assemble(); if (!_machine.Halted) _history.Push(_machine.InstructionIndex); _machine.Step(); Status.Text = _machine.Halted ? "Finished" : "Stepped"; RefreshDisplay(); });
    private void Run_Click(object? s, RoutedEventArgs e) => Try(() => { if (_program is null) Assemble(); _machine.Run(); Status.Text = "Finished"; WorkspaceTabs.SelectedIndex = 1; RefreshDisplay(); });
    private void Reset_Click(object? s, RoutedEventArgs e) => Try(Assemble);
    private void Back_Click(object? s, RoutedEventArgs e) { Messages.Text = "Backstep state restoration is not implemented yet."; Status.Text = "Backstep not yet implemented"; }
    private void RegisterFormat_SelectionChanged(object? s, SelectionChangedEventArgs e) { if (RegistersList is not null) RefreshDisplay(); }
    private void ThemeMode_SelectionChanged(object? s, SelectionChangedEventArgs e) { if (ThemeMode is null) return; RequestedThemeVariant = ThemeMode.SelectedIndex switch { 1 => ThemeVariant.Light, 2 => ThemeVariant.Dark, _ => ThemeVariant.Default }; foreach (var d in _documents) d.Editor.TextArea.TextView.Redraw(); }
    private void Window_KeyDown(object? s, KeyEventArgs e) { if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.N) { New_Click(s, new RoutedEventArgs()); e.Handled = true; } else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.O) { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) OpenFolder_Click(s, new RoutedEventArgs()); else Open_Click(s, new RoutedEventArgs()); e.Handled = true; } else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S) { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) SaveAs_Click(s, new RoutedEventArgs()); else Save_Click(s, new RoutedEventArgs()); e.Handled = true; } else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.W) { CloseFile_Click(s, new RoutedEventArgs()); e.Handled = true; } else if (e.Key == Key.F5) { Run_Click(s, new RoutedEventArgs()); e.Handled = true; } else if (e.Key == Key.F10) { Step_Click(s, new RoutedEventArgs()); e.Handled = true; } }
    private void Try(Action a) { try { a(); } catch (Exception ex) { ShowError(ex); } }
    private void ShowError(Exception ex) { DiagnosticLog.Error("UI operation failed", ex); Status.Text = "Error"; Messages.Text = ex.Message; OutputTabs.SelectedIndex = 0; RefreshDisplay(); }
    private void RefreshDisplay() { if (RegistersList is null) return; var rows = new List<string>(); for (var i = 0; i < 32; i++) rows.Add($"{RegisterFile.Names[i],5}  {FormatRegister(_machine.Registers[i])}"); rows.Add($"   HI  {FormatRegister(_machine.Registers.HI)}"); rows.Add($"   LO  {FormatRegister(_machine.Registers.LO)}"); RegistersList.ItemsSource = rows; PcText.Text = $"PC  0x{_machine.PC:X8}"; Console.Text = _machine.ConsoleText ?? ""; RefreshTextSegment(); HighlightCurrentSourceLine(); }
    private void RefreshTextSegment() { if (TextSegmentGrid is null) return; if (_program is null) { TextSegmentGrid.ItemsSource = Array.Empty<TextRow>(); return; } var sourceLines = (ActiveEditor?.Text ?? "").Replace("\r\n", "\n").Split('\n'); var data = _program.Instructions.Select((x, i) => new TextRow($"0x{0x00400000u + (uint)(i * 4):X8}", "—", x.BasicSource, x.Line > 0 && x.Line <= sourceLines.Length ? $"{x.Line}: {sourceLines[x.Line - 1].Trim()}" : $"Line {x.Line}")).ToList(); TextSegmentGrid.ItemsSource = data; var index = !_machine.Halted && _machine.InstructionIndex < data.Count ? _machine.InstructionIndex : -1; TextSegmentGrid.SelectedIndex = index; if (index >= 0) TextSegmentGrid.ScrollIntoView(data[index], null); }
    private void HighlightCurrentSourceLine() { var editor = ActiveEditor; if (editor is null || _program is null || _machine.Halted || _machine.InstructionIndex >= _program.Instructions.Count || editor.Document is null) return; var lineNo = _program.Instructions[_machine.InstructionIndex].Line; if (lineNo < 1 || lineNo > editor.Document.LineCount) return; var line = editor.Document.GetLineByNumber(lineNo); editor.Select(line.Offset, line.Length); editor.ScrollToLine(lineNo); }
    private string FormatRegister(int value) { var mode = RegisterFormat?.SelectedIndex ?? 0; var u = unchecked((uint)value); return mode switch { 1 => value.ToString(), 2 => u.ToString(), 3 => Convert.ToString(u, 2).PadLeft(32, '0'), 4 => FormatAscii(u), _ => $"0x{u:X8}" }; }
    private static string FormatAscii(uint value) { var b = (byte)(value & 0xFF); return b switch { 0 => "'\\0'", 9 => "'\\t'", 10 => "'\\n'", 13 => "'\\r'", >= 32 and <= 126 => $"'{(char)b}'", _ => $"'\\x{b:X2}'" }; }
    private sealed record TextRow(string Address, string Code, string Basic, string Source);
    private sealed class EditorDocument { public string? Path; public string Name; public TextEditor Editor; public bool Dirty; public EditorDocument(string? path, string name, TextEditor editor, bool dirty) { Path = path; Name = name; Editor = editor; Dirty = dirty; } }
}

internal sealed class AppSettings
{
    public bool ShowLineNumbers { get; set; } = true;
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
