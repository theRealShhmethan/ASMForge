using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using ASMForge.Core.Assembly;
using ASMForge.Core.Cpu;
using ASMForge.Core.Execution;

namespace ASMForge.App.Views;

public partial class MainWindow : Window
{
    private readonly SimpleAssembler _assembler = new();
    private readonly MipsMachine _machine = new();
    private AssemblyProgram? _program;
    private readonly Stack<int> _history = new();
    private readonly List<EditorDocument> _documents = new();
    private string? _projectFolder;
    private const string Sample = "# ASMForge v0.5 sample\nli $t0, 10\nli $t1, 3\nrem $t2, $t0, $t1\n\nmove $a0, $t2\nli $v0, 1\nsyscall\nli $v0, 10\nsyscall\n";

    public MainWindow()
    {
        InitializeComponent();
        OpenDocument(null, "main.asm", Sample, false);
        RefreshExplorer(); RefreshDisplay();
    }

    private EditorDocument? ActiveDocument => EditorTabs.SelectedIndex >= 0 && EditorTabs.SelectedIndex < _documents.Count ? _documents[EditorTabs.SelectedIndex] : null;
    private TextBox? ActiveEditor => ActiveDocument?.Editor;

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
                File.WriteAllText(Path.Combine(folder, r.Name + ".asmforge"), $"{{\n  \"name\": \"{r.Name.Replace("\"", "") }\",\n  \"version\": 1\n}}\n");
                var main = Path.Combine(folder, "main.asm"); if (!File.Exists(main)) File.WriteAllText(main, "# " + r.Name + "\n.text\nmain:\n    li $v0, 10\n    syscall\n");
                _projectFolder = folder; CloseAllDocuments(); OpenFilePath(main); RefreshExplorer(); Status.Text = $"Created project {r.Name}";
            }
            else
            {
                var ext = r.Kind == 1 ? ".cs" : ".asm"; var name = r.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? r.Name : r.Name + ext;
                Directory.CreateDirectory(r.Location); var path = Path.Combine(r.Location, name);
                if (!File.Exists(path)) File.WriteAllText(path, r.Kind == 1 ? "using System;\n\nclass Program\n{\n    static void Main()\n    {\n    }\n}\n" : "# ASMForge assembly file\n.text\nmain:\n");
                OpenFilePath(path); Status.Text = $"Created {name}";
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void Open_Click(object? s, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open source file", AllowMultiple = true, FileTypeFilter = new[] { new FilePickerFileType("Source files") { Patterns = new[] { "*.asm", "*.s", "*.cs" } }, FilePickerFileTypes.All } });
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
        var doc = ActiveDocument; if (doc is null) return;
        var path = doc.Path;
        if (saveAs || path is null)
        {
            var suggested = doc.Name;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save file", SuggestedFileName = suggested });
            if (file is null) return; path = file.Path.LocalPath;
        }
        try { File.WriteAllText(path!, doc.Editor.Text ?? ""); doc.Path = path; doc.Name = Path.GetFileName(path); doc.Dirty = false; UpdateTabHeaders(); RefreshExplorer(); Status.Text = $"Saved {doc.Name}"; }
        catch (Exception ex) { ShowError(ex); }
    }
    private void CloseFile_Click(object? s, RoutedEventArgs e) { var i = EditorTabs.SelectedIndex; if (i < 0) return; _documents.RemoveAt(i); EditorTabs.Items.RemoveAt(i); if (_documents.Count == 0) OpenDocument(null, "Untitled.asm", "", true); }

    private void OpenFilePath(string path)
    {
        var existing = _documents.FindIndex(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)); if (existing >= 0) { EditorTabs.SelectedIndex = existing; return; }
        OpenDocument(path, Path.GetFileName(path), File.ReadAllText(path), false);
    }
    private void OpenDocument(string? path, string name, string text, bool dirty)
    {
        var editor = new TextBox { Text = text, AcceptsReturn = true, AcceptsTab = true, FontFamily = new Avalonia.Media.FontFamily("Cascadia Mono,Consolas"), FontSize = 15, TextWrapping = Avalonia.Media.TextWrapping.NoWrap };
        var doc = new EditorDocument(path, name, editor, dirty); editor.TextChanged += (_, _) => { doc.Dirty = true; UpdateTabHeaders(); _program = null; };
        _documents.Add(doc); var tab = new TabItem { Header = name, Content = editor }; EditorTabs.Items.Add(tab); EditorTabs.SelectedIndex = _documents.Count - 1; UpdateTabHeaders();
    }
    private void CloseAllDocuments() { _documents.Clear(); EditorTabs.Items.Clear(); }
    private void UpdateTabHeaders() { for (var i = 0; i < _documents.Count && i < EditorTabs.Items.Count; i++) if (EditorTabs.Items[i] is TabItem t) t.Header = _documents[i].Name + (_documents[i].Dirty ? " *" : ""); }
    private void RefreshExplorer()
    {
        if (ExplorerList is null) return; var items = new List<string>();
        if (_projectFolder is not null && Directory.Exists(_projectFolder))
        {
            items.Add("▼ " + Path.GetFileName(_projectFolder));
            foreach (var f in Directory.EnumerateFiles(_projectFolder, "*", SearchOption.AllDirectories).Where(p => p.EndsWith(".asm", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".s", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".asmforge", StringComparison.OrdinalIgnoreCase))) items.Add("   " + Path.GetRelativePath(_projectFolder, f));
        }
        else items.Add("No project/folder open"); ExplorerList.ItemsSource = items;
    }
    private void ExplorerList_DoubleTapped(object? s, TappedEventArgs e)
    {
        if (_projectFolder is null || ExplorerList.SelectedItem is not string item) return; var rel = item.Trim(); if (rel.StartsWith("▼") || rel == "No project/folder open") return; var path = Path.Combine(_projectFolder, rel); if (File.Exists(path) && !path.EndsWith(".asmforge", StringComparison.OrdinalIgnoreCase)) OpenFilePath(path);
    }
    private void EditorTabs_SelectionChanged(object? s, SelectionChangedEventArgs e) { _program = null; Status.Text = ActiveDocument is null ? "Ready" : ActiveDocument.Name; }

    private void Assemble()
    {
        var editor = ActiveEditor ?? throw new InvalidOperationException("No file is open.");
        if (ActiveDocument?.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true) throw new InvalidOperationException("C# execution is not implemented yet. ASMForge can create, open, edit, and save C# files in v0.5.");
        _program = _assembler.Assemble(editor.Text ?? ""); _machine.Load(_program); _history.Clear();
        Status.Text = $"Assembled {_program.Instructions.Count} basic instruction(s)"; Messages.Text = $"Assemble completed successfully.\n{_program.Instructions.Count} basic instruction(s) generated."; Console.Text = ""; WorkspaceTabs.SelectedIndex = 1; RefreshDisplay();
    }
    private void Assemble_Click(object? s, RoutedEventArgs e) => Try(Assemble);
    private void Step_Click(object? s, RoutedEventArgs e) => Try(() => { if (_program is null) Assemble(); if (!_machine.Halted) _history.Push(_machine.InstructionIndex); _machine.Step(); Status.Text = _machine.Halted ? "Finished" : "Stepped"; RefreshDisplay(); });
    private void Run_Click(object? s, RoutedEventArgs e) => Try(() => { if (_program is null) Assemble(); _machine.Run(); Status.Text = "Finished"; WorkspaceTabs.SelectedIndex = 1; RefreshDisplay(); });
    private void Reset_Click(object? s, RoutedEventArgs e) => Try(Assemble);
    private void Back_Click(object? s, RoutedEventArgs e) { Messages.Text = "Backstep state restoration is not implemented yet."; Status.Text = "Backstep not yet implemented"; }
    private void RegisterFormat_SelectionChanged(object? s, SelectionChangedEventArgs e) { if (RegistersList is not null) RefreshDisplay(); }
    private void ThemeMode_SelectionChanged(object? s, SelectionChangedEventArgs e) { if (ThemeMode is null) return; RequestedThemeVariant = ThemeMode.SelectedIndex switch { 1 => ThemeVariant.Light, 2 => ThemeVariant.Dark, _ => ThemeVariant.Default }; }
    private void Window_KeyDown(object? s, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.N) { New_Click(s, new RoutedEventArgs()); e.Handled = true; }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S) { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) SaveAs_Click(s, new RoutedEventArgs()); else Save_Click(s, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.F5) { Run_Click(s, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.F10) { Step_Click(s, new RoutedEventArgs()); e.Handled = true; }
    }
    private void Try(Action a) { try { a(); } catch (Exception ex) { ShowError(ex); } }
    private void ShowError(Exception ex) { Status.Text = "Error"; Messages.Text = ex.Message; OutputTabs.SelectedIndex = 0; RefreshDisplay(); }
    private void RefreshDisplay()
    {
        if (RegistersList is null) return; var rows = new List<string>(); for (var i = 0; i < 32; i++) rows.Add($"{RegisterFile.Names[i],5}  {FormatRegister(_machine.Registers[i])}"); rows.Add($"   HI  {FormatRegister(_machine.Registers.HI)}"); rows.Add($"   LO  {FormatRegister(_machine.Registers.LO)}"); RegistersList.ItemsSource = rows; PcText.Text = $"PC  0x{_machine.PC:X8}"; Console.Text = _machine.ConsoleText ?? ""; RefreshTextSegment(); HighlightCurrentSourceLine();
    }
    private void RefreshTextSegment()
    {
        if (TextSegmentGrid is null) return; if (_program is null) { TextSegmentGrid.ItemsSource = Array.Empty<TextRow>(); return; } var sourceLines = (ActiveEditor?.Text ?? "").Replace("\r\n", "\n").Split('\n'); var data = _program.Instructions.Select((x, i) => new TextRow($"0x{0x00400000u + (uint)(i * 4):X8}", "—", x.BasicSource, x.Line > 0 && x.Line <= sourceLines.Length ? $"{x.Line}: {sourceLines[x.Line - 1].Trim()}" : $"Line {x.Line}")).ToList(); TextSegmentGrid.ItemsSource = data; var index = !_machine.Halted && _machine.InstructionIndex < data.Count ? _machine.InstructionIndex : -1; TextSegmentGrid.SelectedIndex = index; if (index >= 0) TextSegmentGrid.ScrollIntoView(data[index], null);
    }
    private void HighlightCurrentSourceLine()
    {
        var editor = ActiveEditor; if (editor is null || _program is null || _machine.Halted || _machine.InstructionIndex >= _program.Instructions.Count || editor.Text is null) return; var line = _program.Instructions[_machine.InstructionIndex].Line; var text = editor.Text; var start = 0; for (var current = 1; current < line; current++) { var n = text.IndexOf('\n', start); if (n < 0) return; start = n + 1; } var end = text.IndexOf('\n', start); if (end < 0) end = text.Length; editor.SelectionStart = start; editor.SelectionEnd = end;
    }
    private string FormatRegister(int value) { var mode = RegisterFormat?.SelectedIndex ?? 0; var u = unchecked((uint)value); return mode switch { 1 => value.ToString(), 2 => u.ToString(), 3 => Convert.ToString(u, 2).PadLeft(32, '0'), 4 => FormatAscii(u), _ => $"0x{u:X8}" }; }
    private static string FormatAscii(uint value) { var b = (byte)(value & 0xFF); return b switch { 0 => "'\\0'", 9 => "'\\t'", 10 => "'\\n'", 13 => "'\\r'", >= 32 and <= 126 => $"'{(char)b}'", _ => $"'\\x{b:X2}'" }; }
    private sealed record TextRow(string Address, string Code, string Basic, string Source);
    private sealed class EditorDocument { public string? Path; public string Name; public TextBox Editor; public bool Dirty; public EditorDocument(string? path, string name, TextBox editor, bool dirty) { Path = path; Name = name; Editor = editor; Dirty = dirty; } }
}
