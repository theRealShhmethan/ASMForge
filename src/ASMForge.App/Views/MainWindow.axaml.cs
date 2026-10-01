using Avalonia.Controls;
using Avalonia.Interactivity;
using ASMForge.Core.Assembly;
using ASMForge.Core.Cpu;
using ASMForge.Core.Execution;

namespace ASMForge.App.Views;

public partial class MainWindow : Window
{
    private readonly SimpleAssembler _assembler = new();
    private readonly MipsMachine _machine = new();
    private AssemblyProgram? _program;

    private const string Sample = "# ASMForge v0.2 sample\n# Registers and memory below are simulated.\nli $t0, 5\nli $t1, 6\nadd $t2, $t0, $t1\n\n# print result\nmove $a0, $t2\nli $v0, 1\nsyscall\nli $v0, 10\nsyscall\n";

    public MainWindow()
    {
        InitializeComponent();
        Editor.Text = Sample;
        RefreshDisplay();
    }

    private void Assemble()
    {
        _program = _assembler.Assemble(Editor.Text ?? "");
        _machine.Load(_program);
        Status.Text = $"Assembled {_program.Instructions.Count} instruction(s)";
        Console.Text = "";
        RefreshDisplay();
    }

    private void Assemble_Click(object? sender, RoutedEventArgs e) => Try(Assemble);

    private void Step_Click(object? sender, RoutedEventArgs e) => Try(() =>
    {
        if (_program is null) Assemble();
        _machine.Step();
        Status.Text = _machine.Halted ? "Finished" : "Stepped";
        RefreshDisplay();
    });

    private void Run_Click(object? sender, RoutedEventArgs e) => Try(() =>
    {
        if (_program is null) Assemble();
        _machine.Run();
        Status.Text = "Finished";
        RefreshDisplay();
    });

    private void Reset_Click(object? sender, RoutedEventArgs e) => Try(Assemble);

    private void RegisterFormat_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (RegistersList is not null)
            RefreshDisplay();
    }

    private void Try(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            Status.Text = "Error";
            Console.Text = ex.Message;
            RefreshDisplay();
        }
    }

    private void RefreshDisplay()
    {
        var rows = new List<string>();
        for (var i = 0; i < 32; i++)
            rows.Add($"{RegisterFile.Names[i],5}  {FormatRegister(_machine.Registers[i])}");

        RegistersList.ItemsSource = rows;
        PcText.Text = $"PC  0x{_machine.PC:X8}";
        if (!string.IsNullOrEmpty(_machine.ConsoleText))
            Console.Text = _machine.ConsoleText;
    }

    private string FormatRegister(int value)
    {
        var mode = RegisterFormat?.SelectedIndex ?? 0;
        var unsigned = unchecked((uint)value);
        return mode switch
        {
            1 => value.ToString(),
            2 => unsigned.ToString(),
            3 => Convert.ToString(unsigned, 2).PadLeft(32, '0'),
            4 => FormatAscii(unsigned),
            _ => $"0x{unsigned:X8}"
        };
    }

    private static string FormatAscii(uint value)
    {
        var b = (byte)(value & 0xFF);
        return b switch
        {
            0 => "'\\0'",
            9 => "'\\t'",
            10 => "'\\n'",
            13 => "'\\r'",
            >= 32 and <= 126 => $"'{(char)b}'",
            _ => $"'\\x{b:X2}'"
        };
    }
}
