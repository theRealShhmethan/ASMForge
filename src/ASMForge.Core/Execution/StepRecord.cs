namespace ASMForge.Core.Execution;

/// <summary>
/// One register write made by a single simulated instruction.
/// Register is 0-31 for general-purpose registers, <see cref="Cpu.RegisterFile.HiIndex"/> or <see cref="Cpu.RegisterFile.LoIndex"/>.
/// </summary>
public readonly record struct RegisterChange(int Register, int OldValue, int NewValue);

/// <summary>One byte written to simulated memory by a single simulated instruction.</summary>
public readonly record struct MemoryChange(uint Address, byte OldValue, byte NewValue);

/// <summary>
/// Everything one executed instruction changed. Used for changed-value highlighting
/// and for restoring the previous machine state when stepping backwards.
/// Changes are listed in the order they were written.
/// </summary>
public sealed record StepRecord(
    uint Pc,
    int InstructionIndex,
    uint PcAfter,
    IReadOnlyList<RegisterChange> RegisterChanges,
    IReadOnlyList<MemoryChange> MemoryChanges,
    uint HeapBreakBefore,
    uint HeapBreakAfter,
    int ConsoleLengthBefore,
    int ExitCodeBefore,
    bool Faulted);

/// <summary>Collects writes while a single instruction executes.</summary>
internal sealed class StepJournal
{
    public List<RegisterChange> Registers { get; } = new();
    public List<MemoryChange> Memory { get; } = new();

    public void Clear()
    {
        Registers.Clear();
        Memory.Clear();
    }
}
