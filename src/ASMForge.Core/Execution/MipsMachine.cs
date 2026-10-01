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
    // Console input: typed lines (each ending in '\n') and how far read syscalls have consumed them.
    private string _input = string.Empty;
    private int _inputPosition;
    // Syscall 40-42 random generators by id (MARS keeps one per id).
    private readonly Dictionary<int, Random> _random = new();
    private readonly StepJournal _journal = new();
    private readonly LinkedList<StepRecord> _history = new();
    private int _historyLimit = 5000;

    /// <summary>The most recently executed (or, after <see cref="StepBack"/>, undone) step.</summary>
    public StepRecord? LastStep { get; private set; }
    public bool LastStepWasUndo { get; private set; }
    public bool CanStepBack => _history.Count > 0;
    public int HistoryCount => _history.Count;

    /// <summary>Maximum number of steps kept for <see cref="StepBack"/>. Oldest steps are dropped first.</summary>
    public int HistoryLimit
    {
        get => _historyLimit;
        set { _historyLimit = Math.Max(0, value); TrimHistory(); }
    }

    /// <summary>True when a read syscall is waiting for <see cref="ProvideInput"/>; the PC has not advanced.</summary>
    public bool WaitingForInput { get; private set; }

    /// <summary>The kind of value the waiting read syscall expects, or null when not waiting.</summary>
    public InputKind? PendingInputKind { get; private set; }

    /// <summary>Console input that has been provided but not read yet.</summary>
    public string UnreadInput => _input[_inputPosition..];

    /// <summary>
    /// Supplies console input for read syscalls (5, 8, 12). Each line is one read; a trailing newline is added
    /// if missing. Clears the waiting state so the next Step/Run retries the read.
    /// </summary>
    public void ProvideInput(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _input += text.EndsWith('\n') ? text : text + "\n";
        WaitingForInput = false;
        PendingInputKind = null;
    }

    public void Load(AssemblyProgram program)
    {
        Program = program;
        _input = string.Empty;
        _inputPosition = 0;
        WaitingForInput = false;
        PendingInputKind = null;
        _random.Clear();
        _history.Clear();
        LastStep = null;
        LastStepWasUndo = false;
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
        var pc = PC;
        var nextPc = unchecked(PC + 4);
        var heapBefore = _heapBreak;
        var consoleBefore = ConsoleText.Length;
        var exitCodeBefore = ExitCode;
        var inputBefore = _inputPosition;
        var completed = false;
        WaitingForInput = false;
        PendingInputKind = null;

        _journal.Clear();
        Registers.Journal = _journal;
        Memory.Journal = _journal;
        try
        {
            Execute(x, ref nextPc);
            if (WaitingForInput)
            {
                // A read syscall found no input: nothing changed and the PC stays on the syscall.
                completed = true;
                return;
            }
            if (!Halted)
            {
                PC = nextPc;
                SyncInstructionIndex();
                if (!Program.AddressToInstruction.ContainsKey(PC)) Halted = true;
            }
            completed = true;
        }
        catch (Exception e) when (e is not InvalidOperationException || !e.Message.StartsWith("Line ", StringComparison.Ordinal))
        {
            Halted = true;
            throw new InvalidOperationException($"Line {x.Line}: {x.Source}\n{e.Message}", e);
        }
        finally
        {
            Registers.Journal = null;
            Memory.Journal = null;
            // Faulting steps are recorded too, so Step Back can return to the state before the error.
            if (!WaitingForInput)
                Record(new StepRecord(pc, index, PC, _journal.Registers.ToArray(), _journal.Memory.ToArray(),
                    heapBefore, _heapBreak, consoleBefore, exitCodeBefore, !completed, inputBefore, _inputPosition));
        }
    }

    /// <summary>Sets a register (0-31, HI, LO) as an undoable edit; Step Back reverts it.</summary>
    public void EditRegister(int index, int value)
    {
        if (index == 0) throw new InvalidOperationException("$zero is always 0 and cannot be edited.");
        RecordEdit(() => Registers.SetByIndex(index, value));
    }

    /// <summary>Writes a word of memory as an undoable edit; Step Back reverts it.</summary>
    public void EditMemoryWord(uint address, uint value) => RecordEdit(() => Memory.WriteWord(address, value));

    private void RecordEdit(Action edit)
    {
        _journal.Clear();
        Registers.Journal = _journal;
        Memory.Journal = _journal;
        try
        {
            edit();
        }
        finally
        {
            Registers.Journal = null;
            Memory.Journal = null;
        }
        Record(new StepRecord(PC, InstructionIndex, PC, _journal.Registers.ToArray(), _journal.Memory.ToArray(),
            _heapBreak, _heapBreak, ConsoleText.Length, ExitCode, false, _inputPosition, _inputPosition, Halted, IsEdit: true));
    }

    /// <summary>
    /// Undoes the most recent recorded step: registers, HI/LO, memory, PC, heap break,
    /// console output and exit state. Returns false when there is nothing to undo.
    /// </summary>
    public bool StepBack()
    {
        var node = _history.Last;
        if (node is null) return false;
        _history.RemoveLast();
        var record = node.Value;

        // Restore in reverse write order so repeated writes to one location unwind correctly.
        for (var i = record.MemoryChanges.Count - 1; i >= 0; i--)
            Memory.WriteByte(record.MemoryChanges[i].Address, record.MemoryChanges[i].OldValue);
        for (var i = record.RegisterChanges.Count - 1; i >= 0; i--)
            Registers.SetByIndex(record.RegisterChanges[i].Register, record.RegisterChanges[i].OldValue);

        PC = record.Pc;
        InstructionIndex = record.InstructionIndex;
        _heapBreak = record.HeapBreakBefore;
        ConsoleText = ConsoleText[..Math.Min(record.ConsoleLengthBefore, ConsoleText.Length)];
        ExitCode = record.ExitCodeBefore;
        // Undoing a read discards the input it consumed, so stepping forward asks for input again.
        if (record.InputPositionAfter > record.InputPositionBefore && record.InputPositionAfter <= _input.Length)
            _input = _input.Remove(record.InputPositionBefore, record.InputPositionAfter - record.InputPositionBefore);
        _inputPosition = Math.Min(record.InputPositionBefore, _input.Length);
        WaitingForInput = false;
        PendingInputKind = null;
        Halted = record.HaltedBefore;
        LastStep = record;
        LastStepWasUndo = true;
        return true;
    }

    private void Record(StepRecord record)
    {
        LastStep = record;
        LastStepWasUndo = false;
        if (_historyLimit == 0) return;
        _history.AddLast(record);
        TrimHistory();
    }

    private void TrimHistory()
    {
        while (_history.Count > _historyLimit) _history.RemoveFirst();
    }

    public void Run(int max = 1_000_000)
    {
        for (var i = 0; i < max && !Halted; i++)
        {
            Step();
            if (WaitingForInput)
                throw new InvalidOperationException($"The program is waiting for console input ({PendingInputKind}). Call ProvideInput before running.");
        }
        if (!Halted) throw new InvalidOperationException("Execution limit reached.");
    }

    /// <summary>
    /// Runs until the program halts, a breakpoint or <paramref name="runToAddress"/> is reached,
    /// <paramref name="cancellation"/> is requested, or <paramref name="maxInstructions"/> have executed.
    /// The instruction at the starting PC always executes, so continuing from a breakpoint makes progress.
    /// Runtime errors propagate as exceptions, exactly like <see cref="Step"/>.
    /// </summary>
    public StopReason RunUntil(int maxInstructions, IReadOnlySet<uint>? breakpoints = null, uint? runToAddress = null,
        CancellationToken cancellation = default)
    {
        for (var i = 0; i < maxInstructions; i++)
        {
            if (Halted) return StopReason.Halted;
            if (i > 0)
            {
                if (runToAddress == PC) return StopReason.RunToTarget;
                if (breakpoints is not null && breakpoints.Contains(PC)) return StopReason.Breakpoint;
            }
            if (cancellation.IsCancellationRequested) return StopReason.Paused;
            Step();
            if (WaitingForInput) return StopReason.WaitingForInput;
        }
        return Halted ? StopReason.Halted : StopReason.LimitReached;
    }

    private void Execute(Instruction x, ref uint nextPc)
    {
        var a = x.Args;
        switch (x.Op.ToLowerInvariant())
        {
            case "nop": return;
            case "syscall": Syscall(); return;
            case "break":
                // MARS's div/divu/rem/remu pseudo-instructions guard the divisor with a branch over "break".
                throw new InvalidOperationException(DivisionSource.IsMatch(x.Source)
                    ? "Division by zero (break instruction executed)."
                    : "break instruction executed.");

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

            case "beq": if (V(a, 0) == V(a, 1)) nextPc = BranchTarget(a[2]); return;
            case "bne": if (V(a, 0) != V(a, 1)) nextPc = BranchTarget(a[2]); return;
            case "bgez": if (V(a, 0) >= 0) nextPc = BranchTarget(a[1]); return;
            case "bgtz": if (V(a, 0) > 0) nextPc = BranchTarget(a[1]); return;
            case "blez": if (V(a, 0) <= 0) nextPc = BranchTarget(a[1]); return;
            case "bltz": if (V(a, 0) < 0) nextPc = BranchTarget(a[1]); return;
            case "bgezal": if (V(a, 0) >= 0) { Registers[31] = unchecked((int)(PC + 4)); nextPc = BranchTarget(a[1]); } return;
            case "bltzal": if (V(a, 0) < 0) { Registers[31] = unchecked((int)(PC + 4)); nextPc = BranchTarget(a[1]); } return;

            // Common MARS branch pseudo-ops are kept source-level for readable stepping.
            case "bgt": if (V(a, 0) > V(a, 1)) nextPc = BranchTarget(a[2]); return;
            case "bge": if (V(a, 0) >= V(a, 1)) nextPc = BranchTarget(a[2]); return;
            case "blt": if (V(a, 0) < V(a, 1)) nextPc = BranchTarget(a[2]); return;
            case "ble": if (V(a, 0) <= V(a, 1)) nextPc = BranchTarget(a[2]); return;
            case "bgtu": if (UV(a, 0) > UV(a, 1)) nextPc = BranchTarget(a[2]); return;
            case "bgeu": if (UV(a, 0) >= UV(a, 1)) nextPc = BranchTarget(a[2]); return;
            case "bltu": if (UV(a, 0) < UV(a, 1)) nextPc = BranchTarget(a[2]); return;
            case "bleu": if (UV(a, 0) <= UV(a, 1)) nextPc = BranchTarget(a[2]); return;

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

    // Branch operands are labels, or a signed instruction offset relative to PC + 4
    // (MARS's generated code uses offsets, e.g. "bne $t2, $zero, 1" in the div expansion).
    private uint BranchTarget(string operand)
    {
        var s = operand.Trim();
        if (Program is not null && Program.Symbols.TryGetValue(s, out var address)) return address;
        if (s.Length > 0 && (char.IsDigit(s[0]) || s[0] is '-' or '+'))
            return unchecked(PC + 4 + (uint)(ParseInt(s) * 4));
        return unchecked((uint)V(s));
    }

    private static readonly System.Text.RegularExpressions.Regex DivisionSource =
        new(@"(^|[\s:])(div|divu|rem|remu)\s", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Takes the next input line for a read syscall and echoes it to the console like a terminal.
    // Returns false (and enters the waiting state) when no complete line has been provided yet.
    private bool TryReadLine(InputKind kind, out string line)
    {
        var newline = _input.IndexOf('\n', _inputPosition);
        if (newline < 0)
        {
            line = string.Empty;
            WaitingForInput = true;
            PendingInputKind = kind;
            return false;
        }
        line = _input[_inputPosition..newline].TrimEnd('\r');
        _inputPosition = newline + 1;
        ConsoleText += line + "\n";
        return true;
    }

    private Random RandomFor(int id)
    {
        if (!_random.TryGetValue(id, out var generator)) _random[id] = generator = new Random();
        return generator;
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
            case 34: ConsoleText += $"0x{unchecked((uint)Registers[4]):X8}"; break;
            case 35: ConsoleText += Convert.ToString(Registers[4], 2).PadLeft(32, '0'); break;
            case 36: ConsoleText += unchecked((uint)Registers[4]).ToString(CultureInfo.InvariantCulture); break;
            case 5: // read integer -> $v0
            {
                if (!TryReadLine(InputKind.Integer, out var line)) return;
                if (!int.TryParse(line.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                        CultureInfo.InvariantCulture, out var value))
                    throw new InvalidOperationException($"Invalid integer input \"{line}\" (syscall 5, read integer).");
                Registers[2] = value;
                break;
            }
            case 8: // read string into buffer $a0 of length $a1, with fgets semantics like MARS
            {
                if (!TryReadLine(InputKind.String, out var line)) return;
                var buffer = unchecked((uint)Registers[4]);
                var length = Registers[5];
                if (length < 1) break;                               // nothing is written
                var text = line + "\n";
                var count = Math.Min(text.Length, length - 1);       // at most n-1 chars; newline kept if it fits
                for (var i = 0; i < count; i++) Memory.WriteByte(buffer + (uint)i, unchecked((byte)text[i]));
                Memory.WriteByte(buffer + (uint)count, 0);
                break;
            }
            case 12: // read character -> $v0 (first character of the line; an empty line reads '\n')
            {
                if (!TryReadLine(InputKind.Character, out var line)) return;
                Registers[2] = line.Length > 0 ? line[0] : '\n';
                break;
            }
            case 30: // system time: milliseconds since 1970, low word in $a0, high word in $a1
            {
                var milliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Registers[4] = unchecked((int)milliseconds);
                Registers[5] = unchecked((int)(milliseconds >> 32));
                break;
            }
            case 40: // set seed: generator id $a0, seed $a1
                _random[Registers[4]] = new Random(Registers[5]);
                break;
            case 41: // random int -> $a0
                Registers[4] = RandomFor(Registers[4]).Next(int.MinValue, int.MaxValue);
                break;
            case 42: // random int in [0, $a1) -> $a0
            {
                var upper = Registers[5];
                if (upper <= 0) throw new InvalidOperationException($"Upper bound of range must be positive (syscall 42, $a1 = {upper}).");
                Registers[4] = RandomFor(Registers[4]).Next(upper);
                break;
            }
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
