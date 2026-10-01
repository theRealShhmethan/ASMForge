using ASMForge.Core.Assembly;
using ASMForge.Core.Cpu;
using ASMForge.Core.Memory;
using System.Globalization;

namespace ASMForge.Core.Execution;

/// <summary>
/// Source-level MIPS32 execution engine. Its behavior is modeled after the MARS
/// simulator while remaining independent from the Avalonia UI.
/// </summary>
public sealed class MipsMachine
{
    public RegisterFile Registers { get; } = new();
    public MipsMemory Memory { get; } = new();
    public AssemblyProgram? Program { get; private set; }
    public int InstructionIndex { get; private set; }
    public bool Halted { get; private set; }
    public string ConsoleText { get; private set; } = string.Empty;
    public uint PC { get; private set; } = AssemblyProgram.DefaultTextBase;
    public int ExitCode { get; private set; }

    private uint _heapBreak = MipsMemory.HeapBase;

    public void Load(AssemblyProgram program)
    {
        Program = program;
        Halted = false;
        ConsoleText = string.Empty;
        ExitCode = 0;
        Registers.Reset();
        Memory.Reset();
        Memory.Load(program.InitialMemory);
        Registers[28] = unchecked((int)0x10008000); // $gp, MARS default
        Registers[29] = unchecked((int)MipsMemory.StackTop); // $sp
        PC = program.EntryPoint;
        _heapBreak = MipsMemory.HeapBase;
        SyncInstructionIndex();
    }

    public void Step()
    {
        if (Halted || Program is null) return;
        if (!Program.AddressToInstruction.TryGetValue(PC, out var index))
        {
            Halted = true;
            return;
        }

        InstructionIndex = index;
        var x = Program.Instructions[index];
        var nextPc = unchecked(PC + 4);

        try
        {
            Execute(x, ref nextPc);
            if (!Halted)
            {
                PC = nextPc;
                SyncInstructionIndex();
                if (!Program.AddressToInstruction.ContainsKey(PC)) Halted = true;
            }
        }
        catch (Exception e) when (e is not InvalidOperationException || !e.Message.StartsWith("Line ", StringComparison.Ordinal))
        {
            Halted = true;
            throw new InvalidOperationException($"Line {x.Line}: {x.Source}\n{e.Message}", e);
        }
    }

    public void Run(int max = 1_000_000)
    {
        for (var i = 0; i < max && !Halted; i++) Step();
        if (!Halted) throw new InvalidOperationException("Execution limit reached.");
    }

    private void Execute(Instruction x, ref uint nextPc)
    {
        var a = x.Args;
        switch (x.Op.ToLowerInvariant())
        {
            case "nop": return;
            case "syscall": Syscall(); return;
            case "break": throw new InvalidOperationException("break instruction executed.");

            case "add": Set(a, 0, CheckedAdd(V(a, 1), V(a, 2))); return;
            case "addu": Set(a, 0, unchecked(V(a, 1) + V(a, 2))); return;
            case "addi": Set(a, 0, CheckedAdd(V(a, 1), V(a, 2))); return;
            case "addiu": Set(a, 0, unchecked(V(a, 1) + V(a, 2))); return;
            case "sub": Set(a, 0, CheckedSub(V(a, 1), V(a, 2))); return;
            case "subu": Set(a, 0, unchecked(V(a, 1) - V(a, 2))); return;
            case "mul": Set(a, 0, unchecked(V(a, 1) * V(a, 2))); return;
            case "mult":
            {
                var product = (long)V(a, 0) * V(a, 1);
                Registers.LO = unchecked((int)product); Registers.HI = unchecked((int)(product >> 32)); return;
            }
            case "multu":
            {
                var product = (ulong)UV(a, 0) * UV(a, 1);
                Registers.LO = unchecked((int)product); Registers.HI = unchecked((int)(product >> 32)); return;
            }
            case "div":
            {
                var dividend = V(a, 0); var divisor = V(a, 1);
                if (divisor == 0) return; // MARS/MIPS: results undefined, no arithmetic exception.
                if (dividend == int.MinValue && divisor == -1) { Registers.LO = int.MinValue; Registers.HI = 0; return; }
                Registers.LO = dividend / divisor; Registers.HI = dividend % divisor; return;
            }
            case "divu":
            {
                var dividend = UV(a, 0); var divisor = UV(a, 1);
                if (divisor == 0) return;
                Registers.LO = unchecked((int)(dividend / divisor)); Registers.HI = unchecked((int)(dividend % divisor)); return;
            }
            case "madd": MultiplyAccumulate(V(a, 0), V(a, 1), false); return;
            case "maddu": MultiplyAccumulateUnsigned(UV(a, 0), UV(a, 1), false); return;
            case "msub": MultiplyAccumulate(V(a, 0), V(a, 1), true); return;
            case "msubu": MultiplyAccumulateUnsigned(UV(a, 0), UV(a, 1), true); return;

            case "and": Set(a, 0, V(a, 1) & V(a, 2)); return;
            case "andi": Set(a, 0, V(a, 1) & (V(a, 2) & 0xffff)); return;
            case "or": Set(a, 0, V(a, 1) | V(a, 2)); return;
            case "ori": Set(a, 0, V(a, 1) | (V(a, 2) & 0xffff)); return;
            case "xor": Set(a, 0, V(a, 1) ^ V(a, 2)); return;
            case "xori": Set(a, 0, V(a, 1) ^ (V(a, 2) & 0xffff)); return;
            case "nor": Set(a, 0, ~(V(a, 1) | V(a, 2))); return;
            case "lui": Set(a, 0, unchecked((int)((uint)(V(a, 1) & 0xffff) << 16))); return;

            case "sll": Set(a, 0, unchecked(V(a, 1) << (V(a, 2) & 31))); return;
            case "sllv": Set(a, 0, unchecked(V(a, 1) << (V(a, 2) & 31))); return;
            case "srl": Set(a, 0, unchecked((int)(UV(a, 1) >> (V(a, 2) & 31)))); return;
            case "srlv": Set(a, 0, unchecked((int)(UV(a, 1) >> (V(a, 2) & 31)))); return;
            case "sra": Set(a, 0, V(a, 1) >> (V(a, 2) & 31)); return;
            case "srav": Set(a, 0, V(a, 1) >> (V(a, 2) & 31)); return;
            case "slt": Set(a, 0, V(a, 1) < V(a, 2) ? 1 : 0); return;
            case "slti": Set(a, 0, V(a, 1) < V(a, 2) ? 1 : 0); return;
            case "sltu": Set(a, 0, UV(a, 1) < UV(a, 2) ? 1 : 0); return;
            case "sltiu": Set(a, 0, UV(a, 1) < unchecked((uint)V(a, 2)) ? 1 : 0); return;
            case "clz": Set(a, 0, CountLeading(UV(a, 1), false)); return;
            case "clo": Set(a, 0, CountLeading(UV(a, 1), true)); return;

            case "mfhi": Set(a, 0, Registers.HI); return;
            case "mflo": Set(a, 0, Registers.LO); return;
            case "mthi": Registers.HI = V(a, 0); return;
            case "mtlo": Registers.LO = V(a, 0); return;

            case "lb": { var m = Mem(a[1]); Set(a, 0, Memory.ReadSByte(m)); return; }
            case "lbu": { var m = Mem(a[1]); Set(a, 0, Memory.ReadByte(m)); return; }
            case "lh": { var m = Mem(a[1]); Set(a, 0, Memory.ReadSignedHalf(m)); return; }
            case "lhu": { var m = Mem(a[1]); Set(a, 0, Memory.ReadHalf(m)); return; }
            case "lw": case "ll": { var m = Mem(a[1]); Set(a, 0, Memory.ReadWord(m)); return; }
            case "sb": { var m = Mem(a[1]); Memory.WriteByte(m, unchecked((byte)V(a, 0))); return; }
            case "sh": { var m = Mem(a[1]); Memory.WriteHalf(m, unchecked((ushort)V(a, 0))); return; }
            case "sw": { var m = Mem(a[1]); Memory.WriteWord(m, V(a, 0)); return; }
            case "sc": { var m = Mem(a[1]); Memory.WriteWord(m, V(a, 0)); Set(a, 0, 1); return; }

            // Unaligned word operations, little-endian behavior compatible with MARS.
            case "lwl": LoadWordLeft(a); return;
            case "lwr": LoadWordRight(a); return;
            case "swl": StoreWordLeft(a); return;
            case "swr": StoreWordRight(a); return;

            case "beq": if (V(a, 0) == V(a, 1)) nextPc = Target(a[2]); return;
            case "bne": if (V(a, 0) != V(a, 1)) nextPc = Target(a[2]); return;
            case "bgez": if (V(a, 0) >= 0) nextPc = Target(a[1]); return;
            case "bgtz": if (V(a, 0) > 0) nextPc = Target(a[1]); return;
            case "blez": if (V(a, 0) <= 0) nextPc = Target(a[1]); return;
            case "bltz": if (V(a, 0) < 0) nextPc = Target(a[1]); return;
            case "bgezal": if (V(a, 0) >= 0) { Registers[31] = unchecked((int)(PC + 4)); nextPc = Target(a[1]); } return;
            case "bltzal": if (V(a, 0) < 0) { Registers[31] = unchecked((int)(PC + 4)); nextPc = Target(a[1]); } return;

            // Common MARS branch pseudo-ops are kept source-level for readable stepping.
            case "bgt": if (V(a, 0) > V(a, 1)) nextPc = Target(a[2]); return;
            case "bge": if (V(a, 0) >= V(a, 1)) nextPc = Target(a[2]); return;
            case "blt": if (V(a, 0) < V(a, 1)) nextPc = Target(a[2]); return;
            case "ble": if (V(a, 0) <= V(a, 1)) nextPc = Target(a[2]); return;
            case "bgtu": if (UV(a, 0) > UV(a, 1)) nextPc = Target(a[2]); return;
            case "bgeu": if (UV(a, 0) >= UV(a, 1)) nextPc = Target(a[2]); return;
            case "bltu": if (UV(a, 0) < UV(a, 1)) nextPc = Target(a[2]); return;
            case "bleu": if (UV(a, 0) <= UV(a, 1)) nextPc = Target(a[2]); return;

            case "j": nextPc = Target(a[0]); return;
            case "jal": Registers[31] = unchecked((int)(PC + 4)); nextPc = Target(a[0]); return;
            case "jr": nextPc = unchecked((uint)V(a, 0)); return;
            case "jalr":
                if (a.Length == 1) { Registers[31] = unchecked((int)(PC + 4)); nextPc = unchecked((uint)V(a, 0)); }
                else { var target = unchecked((uint)V(a, 1)); Set(a, 0, unchecked((int)(PC + 4))); nextPc = target; }
                return;

            case "seq": Set(a, 0, V(a, 1) == V(a, 2) ? 1 : 0); return;
            case "sne": Set(a, 0, V(a, 1) != V(a, 2) ? 1 : 0); return;
            case "sgt": Set(a, 0, V(a, 1) > V(a, 2) ? 1 : 0); return;
            case "sge": Set(a, 0, V(a, 1) >= V(a, 2) ? 1 : 0); return;
            case "sle": Set(a, 0, V(a, 1) <= V(a, 2) ? 1 : 0); return;
            case "sgtu": Set(a, 0, UV(a, 1) > UV(a, 2) ? 1 : 0); return;
            case "sgeu": Set(a, 0, UV(a, 1) >= UV(a, 2) ? 1 : 0); return;
            case "sleu": Set(a, 0, UV(a, 1) <= UV(a, 2) ? 1 : 0); return;

            default: throw new NotSupportedException($"Instruction '{x.Op}' is recognized by the source but is not implemented by the execution engine yet.");
        }
    }

    private int V(string[] args, int index) => V(args[index]);
    private uint UV(string[] args, int index) => unchecked((uint)V(args[index]));

    private int V(string token)
    {
        var s = token.Trim();
        if (s.StartsWith('$')) return Registers[RegisterFile.Parse(s)];
        if (Program is not null)
        {
            if (Program.Constants.TryGetValue(s, out var constant)) return unchecked((int)constant);
            if (Program.Symbols.TryGetValue(s, out var symbol)) return unchecked((int)symbol);
            var split = FindSymbolOffset(s);
            if (split > 0)
            {
                var name = s[..split].Trim();
                if (Program.Symbols.TryGetValue(name, out symbol)) return unchecked((int)(symbol + ParseSignedOffset(s[split..])));
            }
        }
        return ParseInt(s);
    }

    private void Set(string[] args, int index, int value) => Registers[RegisterFile.Parse(args[index])] = value;

    private uint Mem(string operand)
    {
        var s = operand.Trim();
        var l = s.LastIndexOf('(');
        var r = s.LastIndexOf(')');
        if (l >= 0 && r > l)
        {
            var offsetPart = s[..l].Trim();
            var offset = offsetPart.Length == 0 ? 0 : V(offsetPart);
            var reg = RegisterFile.Parse(s[(l + 1)..r].Trim());
            return unchecked((uint)(Registers[reg] + offset));
        }
        return unchecked((uint)V(s));
    }

    private uint Target(string operand)
    {
        if (Program is not null && Program.Symbols.TryGetValue(operand.Trim(), out var address)) return address;
        return unchecked((uint)V(operand));
    }

    private void Syscall()
    {
        switch (Registers[2])
        {
            case 1: ConsoleText += Registers[4].ToString(CultureInfo.InvariantCulture); break;
            case 4: ConsoleText += Memory.ReadCString(unchecked((uint)Registers[4])); break;
            case 9:
            {
                var count = Registers[4];
                if (count < 0) throw new InvalidOperationException("sbrk cannot allocate a negative number of bytes.");
                var old = _heapBreak;
                _heapBreak = checked(_heapBreak + (uint)count);
                Registers[2] = unchecked((int)old);
                break;
            }
            case 10: Halted = true; ExitCode = 0; break;
            case 11: ConsoleText += (char)(Registers[4] & 0xff); break;
            case 17: ExitCode = Registers[4]; Halted = true; break;
            case 34: ConsoleText += $"{unchecked((uint)Registers[4]):X8}"; break;
            case 35: ConsoleText += Convert.ToString(Registers[4], 2).PadLeft(32, '0'); break;
            case 36: ConsoleText += unchecked((uint)Registers[4]).ToString(CultureInfo.InvariantCulture); break;
            default: throw new NotSupportedException($"Syscall {Registers[2]} is not implemented yet.");
        }
    }

    private void SyncInstructionIndex()
    {
        if (Program is not null && Program.AddressToInstruction.TryGetValue(PC, out var index)) InstructionIndex = index;
        else InstructionIndex = Program?.Instructions.Count ?? 0;
    }

    private static int CheckedAdd(int left, int right)
    {
        var result = (long)left + right;
        if (result > int.MaxValue || result < int.MinValue) throw new OverflowException("arithmetic overflow");
        return (int)result;
    }

    private static int CheckedSub(int left, int right)
    {
        var result = (long)left - right;
        if (result > int.MaxValue || result < int.MinValue) throw new OverflowException("arithmetic overflow");
        return (int)result;
    }

    private void MultiplyAccumulate(int left, int right, bool subtract)
    {
        var acc = ((long)Registers.HI << 32) | unchecked((uint)Registers.LO);
        var product = (long)left * right;
        var result = subtract ? unchecked(acc - product) : unchecked(acc + product);
        Registers.LO = unchecked((int)result); Registers.HI = unchecked((int)(result >> 32));
    }

    private void MultiplyAccumulateUnsigned(uint left, uint right, bool subtract)
    {
        var acc = ((ulong)unchecked((uint)Registers.HI) << 32) | unchecked((uint)Registers.LO);
        var product = (ulong)left * right;
        var result = subtract ? unchecked(acc - product) : unchecked(acc + product);
        Registers.LO = unchecked((int)result); Registers.HI = unchecked((int)(result >> 32));
    }

    private static int CountLeading(uint value, bool ones)
    {
        var count = 0;
        for (var bit = 31; bit >= 0; bit--)
        {
            var isOne = ((value >> bit) & 1) != 0;
            if (isOne != ones) break;
            count++;
        }
        return count;
    }

    private void LoadWordLeft(string[] a)
    {
        var address = Mem(a[1]);
        var aligned = address & ~3u;
        var old = unchecked((uint)V(a, 0));
        var mem = Memory.ReadWordUnsigned(aligned);
        var shift = (int)((address & 3) * 8);
        var mask = 0x00ffffffu >> shift;
        Set(a, 0, unchecked((int)((old & mask) | (mem << (24 - shift)))));
    }

    private void LoadWordRight(string[] a)
    {
        var address = Mem(a[1]);
        var aligned = address & ~3u;
        var old = unchecked((uint)V(a, 0));
        var mem = Memory.ReadWordUnsigned(aligned);
        var shift = (int)((address & 3) * 8);
        var mask = 0xffffff00u << (24 - shift);
        Set(a, 0, unchecked((int)((old & mask) | (mem >> shift))));
    }

    private void StoreWordLeft(string[] a)
    {
        var address = Mem(a[1]);
        var value = unchecked((uint)V(a, 0));
        for (var i = 0; i <= (int)(address & 3u); i++)
            Memory.WriteByte((address & ~3u) + (uint)i, (byte)(value >> (24 - ((int)(address & 3u) - i) * 8)));
    }

    private void StoreWordRight(string[] a)
    {
        var address = Mem(a[1]);
        var value = unchecked((uint)V(a, 0));
        for (var i = (int)(address & 3); i < 4; i++) Memory.WriteByte((address & ~3u) + (uint)i, (byte)(value >> ((i - (int)(address & 3)) * 8)));
    }

    private static int ParseInt(string s)
    {
        s = s.Trim();
        var sign = 1;
        if (s.StartsWith('-')) { sign = -1; s = s[1..]; }
        else if (s.StartsWith('+')) s = s[1..];
        long value = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(s[2..], 16)
            : s.StartsWith("0b", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(s[2..], 2)
            : long.Parse(s, CultureInfo.InvariantCulture);
        return unchecked((int)(sign * value));
    }

    private static uint ParseSignedOffset(string s) => unchecked((uint)ParseInt(s));
    private static int FindSymbolOffset(string s)
    {
        for (var i = 1; i < s.Length; i++) if (s[i] is '+' or '-') return i;
        return -1;
    }
}
