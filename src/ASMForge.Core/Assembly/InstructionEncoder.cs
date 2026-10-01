using ASMForge.Core.Cpu;
using System.Globalization;
using System.Text;

namespace ASMForge.Core.Assembly;

/// <summary>
/// Encodes basic MIPS32 instructions into 32-bit machine words (R-, I- and J-type, plus SPECIAL2),
/// matching the encodings MARS shows in its Text Segment "Code" column.
/// </summary>
public static class InstructionEncoder
{
    private const uint Special = 0x00, RegImm = 0x01, Special2 = 0x1C;

    // R-type function codes (opcode 0).
    private static readonly Dictionary<string, uint> RFunct = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sll"] = 0x00, ["srl"] = 0x02, ["sra"] = 0x03, ["sllv"] = 0x04, ["srlv"] = 0x06, ["srav"] = 0x07,
        ["jr"] = 0x08, ["jalr"] = 0x09, ["syscall"] = 0x0C, ["break"] = 0x0D,
        ["mfhi"] = 0x10, ["mthi"] = 0x11, ["mflo"] = 0x12, ["mtlo"] = 0x13,
        ["mult"] = 0x18, ["multu"] = 0x19, ["div"] = 0x1A, ["divu"] = 0x1B,
        ["add"] = 0x20, ["addu"] = 0x21, ["sub"] = 0x22, ["subu"] = 0x23,
        ["and"] = 0x24, ["or"] = 0x25, ["xor"] = 0x26, ["nor"] = 0x27, ["slt"] = 0x2A, ["sltu"] = 0x2B
    };

    // SPECIAL2 function codes (opcode 0x1C).
    private static readonly Dictionary<string, uint> Special2Funct = new(StringComparer.OrdinalIgnoreCase)
    {
        ["madd"] = 0x00, ["maddu"] = 0x01, ["mul"] = 0x02, ["msub"] = 0x04, ["msubu"] = 0x05, ["clz"] = 0x20, ["clo"] = 0x21
    };

    // I-type opcodes.
    private static readonly Dictionary<string, uint> IOpcode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["beq"] = 0x04, ["bne"] = 0x05, ["blez"] = 0x06, ["bgtz"] = 0x07,
        ["addi"] = 0x08, ["addiu"] = 0x09, ["slti"] = 0x0A, ["sltiu"] = 0x0B,
        ["andi"] = 0x0C, ["ori"] = 0x0D, ["xori"] = 0x0E, ["lui"] = 0x0F,
        ["lb"] = 0x20, ["lh"] = 0x21, ["lwl"] = 0x22, ["lw"] = 0x23, ["lbu"] = 0x24, ["lhu"] = 0x25, ["lwr"] = 0x26,
        ["sb"] = 0x28, ["sh"] = 0x29, ["swl"] = 0x2A, ["sw"] = 0x2B, ["swr"] = 0x2E, ["ll"] = 0x30, ["sc"] = 0x38
    };

    // REGIMM branches (opcode 1); the condition is selected by the rt field.
    private static readonly Dictionary<string, uint> RegImmRt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bltz"] = 0x00, ["bgez"] = 0x01, ["bltzal"] = 0x10, ["bgezal"] = 0x11
    };

    /// <summary>
    /// Encodes one basic instruction located at <paramref name="address"/>. Branch and jump labels are
    /// resolved through <paramref name="symbols"/>; numeric branch operands are word offsets from PC + 4.
    /// </summary>
    public static uint Encode(string op, IReadOnlyList<string> args, uint address, IReadOnlyDictionary<string, uint> symbols)
    {
        op = op.ToLowerInvariant();
        uint Reg(int i) => (uint)RegisterFile.Parse(args[i]);

        switch (op)
        {
            case "nop": return 0;
            case "syscall": return R(0, 0, 0, 0, RFunct[op]);
            case "break": return R(0, 0, 0, 0, RFunct[op]) | ((args.Count > 0 ? (uint)ParseImmediate(args[0]) : 0u) & 0xFFFFF) << 6;
            case "sll": case "srl": case "sra": return R(0, Reg(1), Reg(0), (uint)ParseImmediate(args[2]) & 0x1F, RFunct[op]);
            case "sllv": case "srlv": case "srav": return R(Reg(2), Reg(1), Reg(0), 0, RFunct[op]);
            case "jr": return R(Reg(0), 0, 0, 0, RFunct[op]);
            case "jalr": return args.Count == 1 ? R(Reg(0), 0, 31, 0, RFunct[op]) : R(Reg(1), 0, Reg(0), 0, RFunct[op]);
            case "mfhi": case "mflo": return R(0, 0, Reg(0), 0, RFunct[op]);
            case "mthi": case "mtlo": return R(Reg(0), 0, 0, 0, RFunct[op]);
            case "mult": case "multu": case "div": case "divu": return R(Reg(0), Reg(1), 0, 0, RFunct[op]);
            case "madd": case "maddu": case "msub": case "msubu": return R(Reg(0), Reg(1), 0, 0, Special2Funct[op], Special2);
            case "mul": return R(Reg(1), Reg(2), Reg(0), 0, Special2Funct[op], Special2);
            case "clz": case "clo": return R(Reg(1), 0, Reg(0), 0, Special2Funct[op], Special2);
            case "j": case "jal":
                return ((op == "j" ? 0x02u : 0x03u) << 26) | ((Resolve(args[0], symbols) >> 2) & 0x03FFFFFF);
            case "beq": case "bne":
                return I(IOpcode[op], Reg(0), Reg(1), BranchOffset(args[2], address, symbols));
            case "blez": case "bgtz":
                return I(IOpcode[op], Reg(0), 0, BranchOffset(args[1], address, symbols));
            case "bltz": case "bgez": case "bltzal": case "bgezal":
                return I(RegImm, Reg(0), RegImmRt[op], BranchOffset(args[1], address, symbols));
            case "lui":
                return I(IOpcode[op], 0, Reg(0), (uint)ParseImmediate(args[1]));
        }

        if (RFunct.TryGetValue(op, out var funct)) return R(Reg(1), Reg(2), Reg(0), 0, funct); // add rd, rs, rt ...

        if (IOpcode.TryGetValue(op, out var opcode))
        {
            var memory = args.Count == 2 ? ParseMemoryOperand(args[1]) : null;
            if (memory is { } m) return I(opcode, (uint)m.Base, Reg(0), (uint)m.Offset); // lw rt, offset(base)
            return I(opcode, Reg(1), Reg(0), (uint)ParseImmediate(args[2]));             // addi rt, rs, imm
        }

        throw new NotSupportedException($"No machine encoding for '{op}'.");
    }

    /// <summary>Instruction format letter for a machine word: R, I or J.</summary>
    public static char FormatOf(uint word) => (word >> 26) switch
    {
        Special or Special2 => 'R',
        0x02 or 0x03 => 'J',
        _ => 'I'
    };

    /// <summary>Multi-line breakdown of a machine word's fields, for tooltips and teaching.</summary>
    public static string Describe(uint word)
    {
        var opcode = word >> 26;
        var rs = (word >> 21) & 0x1F;
        var rt = (word >> 16) & 0x1F;
        var text = new StringBuilder();
        var format = FormatOf(word);
        text.Append(format).Append("-type   0x").Append(word.ToString("X8", CultureInfo.InvariantCulture)).AppendLine();

        switch (format)
        {
            case 'R':
                var rd = (word >> 11) & 0x1F;
                var shamt = (word >> 6) & 0x1F;
                var funct = word & 0x3F;
                text.AppendLine($"opcode  {Bits(opcode, 6)}  ({opcode})");
                text.AppendLine($"rs      {Bits(rs, 5)}   ({RegisterFile.Names[rs]})");
                text.AppendLine($"rt      {Bits(rt, 5)}   ({RegisterFile.Names[rt]})");
                text.AppendLine($"rd      {Bits(rd, 5)}   ({RegisterFile.Names[rd]})");
                text.AppendLine($"shamt   {Bits(shamt, 5)}   ({shamt})");
                text.AppendLine($"funct   {Bits(funct, 6)}  (0x{funct:X2})");
                text.Append($"binary  {Bits(opcode, 6)} {Bits(rs, 5)} {Bits(rt, 5)} {Bits(rd, 5)} {Bits(shamt, 5)} {Bits(funct, 6)}");
                break;
            case 'J':
                var target = word & 0x03FFFFFF;
                text.AppendLine($"opcode  {Bits(opcode, 6)}  ({opcode})");
                text.AppendLine($"target  {Bits(target, 26)}  (address 0x{target << 2:X8})");
                text.Append($"binary  {Bits(opcode, 6)} {Bits(target, 26)}");
                break;
            default:
                var immediate = word & 0xFFFF;
                text.AppendLine($"opcode  {Bits(opcode, 6)}  ({opcode})");
                text.AppendLine($"rs      {Bits(rs, 5)}   ({RegisterFile.Names[rs]})");
                text.AppendLine($"rt      {Bits(rt, 5)}   ({RegisterFile.Names[rt]})");
                text.AppendLine($"imm     {Bits(immediate, 16)}  ({unchecked((short)immediate)}, 0x{immediate:X4})");
                text.Append($"binary  {Bits(opcode, 6)} {Bits(rs, 5)} {Bits(rt, 5)} {Bits(immediate, 16)}");
                break;
        }
        return text.ToString().Replace("\r\n", "\n");
    }

    private static uint R(uint rs, uint rt, uint rd, uint shamt, uint funct, uint opcode = Special) =>
        (opcode << 26) | (rs << 21) | (rt << 16) | (rd << 11) | (shamt << 6) | funct;

    private static uint I(uint opcode, uint rs, uint rt, uint immediate) =>
        (opcode << 26) | (rs << 21) | (rt << 16) | (immediate & 0xFFFF);

    private static uint BranchOffset(string operand, uint address, IReadOnlyDictionary<string, uint> symbols)
    {
        var s = operand.Trim();
        if (symbols.TryGetValue(s, out var target))
            return unchecked((uint)((int)(target - (address + 4)) >> 2));
        return unchecked((uint)ParseImmediate(s)); // already a word offset (generated code)
    }

    private static uint Resolve(string operand, IReadOnlyDictionary<string, uint> symbols) =>
        symbols.TryGetValue(operand.Trim(), out var address) ? address : unchecked((uint)ParseImmediate(operand));

    private static (int Offset, int Base)? ParseMemoryOperand(string operand)
    {
        var open = operand.LastIndexOf('(');
        var close = operand.LastIndexOf(')');
        if (open < 0 || close < open) return null;
        var offsetText = operand[..open].Trim();
        return (offsetText.Length == 0 ? 0 : ParseImmediate(offsetText), RegisterFile.Parse(operand[(open + 1)..close].Trim()));
    }

    private static int ParseImmediate(string text)
    {
        var s = text.Trim();
        var negative = s.StartsWith('-');
        if (negative || s.StartsWith('+')) s = s[1..];
        var value = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt64(s[2..], 16)
            : long.Parse(s, NumberStyles.None, CultureInfo.InvariantCulture);
        return unchecked((int)(negative ? -value : value));
    }

    private static string Bits(uint value, int width) => Convert.ToString(value, 2).PadLeft(width, '0');
}
