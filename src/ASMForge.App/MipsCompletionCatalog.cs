using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using System.Text.RegularExpressions;

namespace ASMForge.App;

internal static class MipsCompletionCatalog
{
    private sealed record Entry(string Text, string Syntax, string Description, string Category);

    private static readonly Entry[] Entries =
    {
        new("add", "add rd, rs, rt", "Add two registers with signed-overflow checking.", "Instruction"),
        new("addu", "addu rd, rs, rt", "Add two registers without signed-overflow trapping.", "Instruction"),
        new("addi", "addi rt, rs, immediate", "Add a signed immediate value to a register.", "Instruction"),
        new("addiu", "addiu rt, rs, immediate", "Add an immediate value without signed-overflow trapping.", "Instruction"),
        new("sub", "sub rd, rs, rt", "Subtract rt from rs with signed-overflow checking.", "Instruction"),
        new("subu", "subu rd, rs, rt", "Subtract rt from rs without signed-overflow trapping.", "Instruction"),
        new("mul", "mul rd, rs, rt", "Pseudo/basic multiply form that stores the low result in rd.", "Instruction"),
        new("mult", "mult rs, rt", "Signed multiply; result is written to HI and LO.", "Instruction"),
        new("multu", "multu rs, rt", "Unsigned multiply; result is written to HI and LO.", "Instruction"),
        new("div", "div rs, rt", "Signed divide; quotient goes to LO and remainder goes to HI.", "Instruction"),
        new("divu", "divu rs, rt", "Unsigned divide; quotient goes to LO and remainder goes to HI.", "Instruction"),
        new("rem", "rem rd, rs, rt", "Pseudo-instruction: stores rs % rt in rd. Expands through div and mfhi.", "Pseudo-instruction"),
        new("and", "and rd, rs, rt", "Bitwise AND of two registers.", "Instruction"),
        new("andi", "andi rt, rs, immediate", "Bitwise AND with a zero-extended immediate.", "Instruction"),
        new("or", "or rd, rs, rt", "Bitwise OR of two registers.", "Instruction"),
        new("ori", "ori rt, rs, immediate", "Bitwise OR with a zero-extended immediate.", "Instruction"),
        new("xor", "xor rd, rs, rt", "Bitwise XOR of two registers.", "Instruction"),
        new("xori", "xori rt, rs, immediate", "Bitwise XOR with a zero-extended immediate.", "Instruction"),
        new("nor", "nor rd, rs, rt", "Bitwise NOR of two registers.", "Instruction"),
        new("sll", "sll rd, rt, shamt", "Shift left logical by a constant shift amount.", "Instruction"),
        new("srl", "srl rd, rt, shamt", "Shift right logical by a constant shift amount.", "Instruction"),
        new("sra", "sra rd, rt, shamt", "Shift right arithmetic by a constant shift amount.", "Instruction"),
        new("slt", "slt rd, rs, rt", "Set rd to 1 when rs is less than rt (signed).", "Instruction"),
        new("slti", "slti rt, rs, immediate", "Set rt to 1 when rs is less than the signed immediate.", "Instruction"),
        new("lw", "lw rt, offset(base)", "Load a 32-bit word from simulated memory.", "Instruction"),
        new("sw", "sw rt, offset(base)", "Store a 32-bit word into simulated memory.", "Instruction"),
        new("lb", "lb rt, offset(base)", "Load a signed byte from simulated memory.", "Instruction"),
        new("lbu", "lbu rt, offset(base)", "Load an unsigned byte from simulated memory.", "Instruction"),
        new("lh", "lh rt, offset(base)", "Load a signed halfword from simulated memory.", "Instruction"),
        new("lhu", "lhu rt, offset(base)", "Load an unsigned halfword from simulated memory.", "Instruction"),
        new("sb", "sb rt, offset(base)", "Store the low byte of rt into simulated memory.", "Instruction"),
        new("sh", "sh rt, offset(base)", "Store the low halfword of rt into simulated memory.", "Instruction"),
        new("li", "li rt, immediate", "Pseudo-instruction: load an immediate value into a register.", "Pseudo-instruction"),
        new("la", "la rt, label", "Pseudo-instruction: load the address of a label.", "Pseudo-instruction"),
        new("move", "move rd, rs", "Pseudo-instruction: copy rs into rd.", "Pseudo-instruction"),
        new("mfhi", "mfhi rd", "Copy the HI register into rd.", "Instruction"),
        new("mflo", "mflo rd", "Copy the LO register into rd.", "Instruction"),
        new("beq", "beq rs, rt, label", "Branch when rs equals rt.", "Instruction"),
        new("bne", "bne rs, rt, label", "Branch when rs does not equal rt.", "Instruction"),
        new("bgt", "bgt rs, rt, label", "Pseudo-instruction: branch when rs is greater than rt.", "Pseudo-instruction"),
        new("bge", "bge rs, rt, label", "Pseudo-instruction: branch when rs is greater than or equal to rt.", "Pseudo-instruction"),
        new("blt", "blt rs, rt, label", "Pseudo-instruction: branch when rs is less than rt.", "Pseudo-instruction"),
        new("ble", "ble rs, rt, label", "Pseudo-instruction: branch when rs is less than or equal to rt.", "Pseudo-instruction"),
        new("j", "j label", "Jump to a label.", "Instruction"),
        new("jal", "jal label", "Jump to a label and store the return address in $ra.", "Instruction"),
        new("jr", "jr rs", "Jump to the address contained in rs.", "Instruction"),
        new("syscall", "syscall", "Invoke a MARS-compatible system service selected by $v0.", "Instruction"),
        new("nop", "nop", "No operation.", "Pseudo-instruction"),

        new(".text", ".text [address]", "Begin the text/code segment.", "Directive"),
        new(".data", ".data [address]", "Begin the static data segment.", "Directive"),
        new(".globl", ".globl symbol", "Make a symbol globally visible.", "Directive"),
        new(".word", ".word value [, value ...]", "Store one or more 32-bit words in the data segment.", "Directive"),
        new(".half", ".half value [, value ...]", "Store one or more 16-bit halfwords.", "Directive"),
        new(".byte", ".byte value [, value ...]", "Store one or more bytes.", "Directive"),
        new(".space", ".space n", "Reserve n bytes in the data segment.", "Directive"),
        new(".ascii", ".ascii \"text\"", "Store a string without a trailing zero byte.", "Directive"),
        new(".asciiz", ".asciiz \"text\"", "Store a zero-terminated string.", "Directive")
    };

    private static readonly string[] Registers =
    {
        "$zero", "$at", "$v0", "$v1", "$a0", "$a1", "$a2", "$a3",
        "$t0", "$t1", "$t2", "$t3", "$t4", "$t5", "$t6", "$t7", "$t8", "$t9",
        "$s0", "$s1", "$s2", "$s3", "$s4", "$s5", "$s6", "$s7",
        "$k0", "$k1", "$gp", "$sp", "$fp", "$ra"
    };

    internal static IReadOnlyList<ICompletionData> GetSuggestions(string prefix, string source)
    {
        var result = new List<ICompletionData>();
        var comparison = StringComparison.OrdinalIgnoreCase;

        foreach (var entry in Entries)
        {
            if (entry.Text.StartsWith(prefix, comparison))
                result.Add(new MipsCompletionData(entry.Text, entry.Syntax, entry.Description, entry.Category));
        }

        if (prefix.StartsWith("$", StringComparison.Ordinal))
        {
            foreach (var register in Registers)
            {
                if (register.StartsWith(prefix, comparison))
                    result.Add(new MipsCompletionData(register, register, "MIPS general-purpose register.", "Register"));
            }
        }

        foreach (Match match in Regex.Matches(source, @"(?m)^\s*([A-Za-z_]\w*)\s*:"))
        {
            var label = match.Groups[1].Value;
            if (label.StartsWith(prefix, comparison) && result.All(x => !string.Equals(x.Text, label, comparison)))
                result.Add(new MipsCompletionData(label, label, "Label defined in the current source file.", "Label"));
        }

        return result.OrderBy(x => x.Text, StringComparer.OrdinalIgnoreCase).Take(30).ToArray();
    }
}

internal sealed class MipsCompletionData : ICompletionData
{
    private readonly string _syntax;
    private readonly string _description;
    private readonly string _category;
    private Control? _content;
    private Control? _descriptionControl;

    internal MipsCompletionData(string text, string syntax, string description, string category)
    {
        Text = text;
        _syntax = syntax;
        _description = description;
        _category = category;
    }

    public IImage? Image => null;
    public string Text { get; }
    public double Priority => 0;

    public object Content => _content ??= new TextBlock
    {
        Text = Text,
        Margin = new Thickness(5, 2)
    };

    public object Description => _descriptionControl ??= new StackPanel
    {
        Margin = new Thickness(8),
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = _category, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = _syntax, FontFamily = new FontFamily("Cascadia Mono,Consolas"), FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = _description, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 }
        }
    };

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        var document = textArea.Document;
        var caret = Math.Clamp(textArea.Caret.Offset, 0, document.TextLength);
        var start = caret;
        while (start > 0)
        {
            var c = document.GetCharAt(start - 1);
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '$' && c != '.')
                break;
            start--;
        }

        document.Replace(start, caret - start, Text);
        textArea.Caret.Offset = start + Text.Length;
    }
}
