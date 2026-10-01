namespace ASMForge.Core.Execution;

/// <summary>Why <see cref="MipsMachine.RunUntil"/> returned.</summary>
public enum StopReason
{
    /// <summary>The program exited or ran past its last instruction.</summary>
    Halted,
    /// <summary>The next instruction to execute has a breakpoint.</summary>
    Breakpoint,
    /// <summary>The requested run-to address (e.g. Run to Cursor) was reached.</summary>
    RunToTarget,
    /// <summary>Cancellation was requested (Pause or Stop).</summary>
    Paused,
    /// <summary>The instruction limit was reached; the program may be in an infinite loop.</summary>
    LimitReached,
    /// <summary>A read syscall needs console input; call ProvideInput and run again.</summary>
    WaitingForInput
}

/// <summary>What kind of value a waiting read syscall expects.</summary>
public enum InputKind
{
    /// <summary>Syscall 5, read integer.</summary>
    Integer,
    /// <summary>Syscall 8, read string.</summary>
    String,
    /// <summary>Syscall 12, read character.</summary>
    Character
}
