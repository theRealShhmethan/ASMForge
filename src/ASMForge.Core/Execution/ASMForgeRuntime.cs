using ASMForge.Core.Assembly;
using ASMForge.Core.Cpu;
using ASMForge.Core.Memory;

namespace ASMForge.Core.Execution;

/// <summary>
/// Public host API for embedding ASMForge's simulated MIPS runtime in C# code.
/// All register and memory access targets the simulated MIPS machine, never native host memory.
/// </summary>
public sealed class ASMForgeRuntime
{
    private readonly SimpleAssembler _assembler = new();
    private readonly MipsMachine _machine = new();
    private AssemblyProgram? _program;

    public ASMForgeRuntime()
    {
        Registers = new RuntimeRegisterAccessor(_machine.Registers);
    }

    public RuntimeRegisterAccessor Registers { get; }
    public MipsMemory Memory => _machine.Memory;
    public MipsMachine Machine => _machine;
    public AssemblyProgram? Program => _program;
    public uint PC => _machine.PC;
    public bool IsHalted => _machine.Halted;
    public bool IsRunning { get; private set; }
    public string Output => _machine.ConsoleText;
    public int ExitCode => _machine.ExitCode;
    public StepRecord? LastStep => _machine.LastStep;
    public bool CanStepBack => _machine.CanStepBack;

    public AssemblyProgram LoadAssembly(string source)
    {
        _program = _assembler.Assemble(source ?? throw new ArgumentNullException(nameof(source)));
        _machine.Load(_program);
        return _program;
    }

    public void Run(int maxInstructions = 1_000_000)
    {
        EnsureLoaded();
        IsRunning = true;
        try
        {
            _machine.Run(maxInstructions);
        }
        finally
        {
            IsRunning = false;
        }
    }

    public void Step()
    {
        EnsureLoaded();
        _machine.Step();
    }

    /// <summary>Undoes the most recent step. Returns false when there is no step history.</summary>
    public bool StepBack()
    {
        EnsureLoaded();
        return _machine.StepBack();
    }

    public void Reset()
    {
        EnsureLoaded();
        _machine.Load(_program!);
    }

    private void EnsureLoaded()
    {
        if (_program is null)
            throw new InvalidOperationException("No assembly program is loaded. Call LoadAssembly first.");
    }
}

/// <summary>String/int indexer wrapper around the simulated MIPS register file.</summary>
public sealed class RuntimeRegisterAccessor
{
    private readonly RegisterFile _registers;

    internal RuntimeRegisterAccessor(RegisterFile registers) => _registers = registers;

    public int this[string register]
    {
        get => _registers[RegisterFile.Parse(register)];
        set => _registers[RegisterFile.Parse(register)] = value;
    }

    public int this[int register]
    {
        get => _registers[register];
        set => _registers[register] = value;
    }

    public int HI { get => _registers.HI; set => _registers.HI = value; }
    public int LO { get => _registers.LO; set => _registers.LO = value; }
}
