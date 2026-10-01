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
    LimitReached
}
