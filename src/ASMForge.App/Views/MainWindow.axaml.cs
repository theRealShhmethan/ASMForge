using Avalonia.Controls;
using Avalonia.Interactivity;
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
    private const string Sample = "# ASMForge v0.4 sample\n# Assemble switches to the MARS-style Execute view.\nli $t0, 10\nli $t1, 3\nrem $t2, $t0, $t1\n\nmove $a0, $t2\nli $v0, 1\nsyscall\nli $v0, 10\nsyscall\n";

    public MainWindow() { InitializeComponent(); Editor.Text = Sample; RefreshDisplay(); }

    private void Assemble()
    {
        _program = _assembler.Assemble(Editor.Text ?? "");
        _machine.Load(_program); _history.Clear();
        Status.Text = $"Assembled {_program.Instructions.Count} basic instruction(s)";
        Messages.Text = $"Assemble: operation completed successfully.\n{_program.Instructions.Count} basic instruction(s) generated.";
        Console.Text = ""; WorkspaceTabs.SelectedIndex = 1; RefreshDisplay();
    }
    private void Assemble_Click(object? s, RoutedEventArgs e) => Try(Assemble);
    private void Step_Click(object? s, RoutedEventArgs e) => Try(() => { if (_program is null) Assemble(); if (!_machine.Halted) _history.Push(_machine.InstructionIndex); _machine.Step(); Status.Text = _machine.Halted ? "Finished" : "Stepped"; RefreshDisplay(); });
    private void Run_Click(object? s, RoutedEventArgs e) => Try(() => { if (_program is null) Assemble(); _machine.Run(); Status.Text = "Finished"; WorkspaceTabs.SelectedIndex = 1; RefreshDisplay(); });
    private void Reset_Click(object? s, RoutedEventArgs e) => Try(Assemble);
    private void Back_Click(object? s, RoutedEventArgs e) { Messages.Text = "Backstep UI is reserved in v0.4. Full state restoration will be implemented with the MARS-compatible debugger history."; Status.Text = "Backstep not yet implemented"; }
    private void RegisterFormat_SelectionChanged(object? s, SelectionChangedEventArgs e) { if (RegistersList is not null) RefreshDisplay(); }
    private void ThemeMode_SelectionChanged(object? s, SelectionChangedEventArgs e) { if (ThemeMode is null) return; RequestedThemeVariant = ThemeMode.SelectedIndex switch { 1 => ThemeVariant.Light, 2 => ThemeVariant.Dark, _ => ThemeVariant.Default }; }
    private void Try(Action a) { try { a(); } catch (Exception ex) { Status.Text = "Error"; Messages.Text = ex.Message; OutputTabs.SelectedIndex = 0; RefreshDisplay(); } }

    private void RefreshDisplay()
    {
        if (RegistersList is null) return;
        var rows = new List<string>();
        for (var i = 0; i < 32; i++) rows.Add($"{RegisterFile.Names[i],5}  {FormatRegister(_machine.Registers[i])}");
        rows.Add($"   HI  {FormatRegister(_machine.Registers.HI)}"); rows.Add($"   LO  {FormatRegister(_machine.Registers.LO)}");
        RegistersList.ItemsSource = rows; PcText.Text = $"PC  0x{_machine.PC:X8}";
        Console.Text = _machine.ConsoleText ?? ""; RefreshTextSegment(); HighlightCurrentSourceLine();
    }

    private void RefreshTextSegment()
    {
        if (TextSegmentGrid is null) return;
        if (_program is null) { TextSegmentGrid.ItemsSource = Array.Empty<TextRow>(); return; }
        var sourceLines = (Editor.Text ?? "").Replace("\r\n", "\n").Split('\n');
        var data = _program.Instructions.Select((x, i) => new TextRow(
            $"0x{0x00400000u + (uint)(i * 4):X8}", "—", x.BasicSource,
            x.Line > 0 && x.Line <= sourceLines.Length ? $"{x.Line}: {sourceLines[x.Line - 1].Trim()}" : $"Line {x.Line}" )).ToList();
        TextSegmentGrid.ItemsSource = data;
        var index = !_machine.Halted && _machine.InstructionIndex < data.Count ? _machine.InstructionIndex : -1;
        TextSegmentGrid.SelectedIndex = index;
        if (index >= 0) TextSegmentGrid.ScrollIntoView(data[index], null);
    }

    private void HighlightCurrentSourceLine()
    {
        if (_program is null || _machine.Halted || _machine.InstructionIndex >= _program.Instructions.Count || Editor.Text is null) { Editor.SelectionStart = Editor.SelectionEnd = 0; return; }
        var line = _program.Instructions[_machine.InstructionIndex].Line; var text = Editor.Text; var start = 0;
        for (var current = 1; current < line; current++) { var n = text.IndexOf('\n', start); if (n < 0) return; start = n + 1; }
        var end = text.IndexOf('\n', start); if (end < 0) end = text.Length; Editor.SelectionStart = start; Editor.SelectionEnd = end;
    }
    private string FormatRegister(int value) { var mode = RegisterFormat?.SelectedIndex ?? 0; var u = unchecked((uint)value); return mode switch { 1 => value.ToString(), 2 => u.ToString(), 3 => Convert.ToString(u, 2).PadLeft(32, '0'), 4 => FormatAscii(u), _ => $"0x{u:X8}" }; }
    private static string FormatAscii(uint value) { var b = (byte)(value & 0xFF); return b switch { 0 => "'\\0'", 9 => "'\\t'", 10 => "'\\n'", 13 => "'\\r'", >= 32 and <= 126 => $"'{(char)b}'", _ => $"'\\x{b:X2}'" }; }
    private sealed record TextRow(string Address, string Code, string Basic, string Source);
}
