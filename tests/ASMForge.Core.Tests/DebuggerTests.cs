using ASMForge.Core.Assembly;
using ASMForge.Core.Execution;
using Xunit;

namespace ASMForge.Core.Tests;

public class DebuggerTests
{
    private const string Program = ".text\nmain:\nli $t0, 1\nmiddle:\nli $t1, 2\nli $t2, 3\nend:\nli $t3, 4";

    private static (MipsMachine Machine, AssemblyProgram Program) Load(string source)
    {
        var program = new SimpleAssembler().Assemble(source);
        var machine = new MipsMachine();
        machine.Load(program);
        return (machine, program);
    }

    [Fact]
    public void RunStopsBeforeExecutingBreakpointInstruction()
    {
        var (m, p) = Load(Program);
        var reason = m.RunUntil(1000, new HashSet<uint> { p.Symbols["middle"] });
        Assert.Equal(StopReason.Breakpoint, reason);
        Assert.Equal(p.Symbols["middle"], m.PC);
        Assert.Equal(1, m.Registers[8]);
        Assert.Equal(0, m.Registers[9]); // breakpoint instruction not executed yet
    }

    [Fact]
    public void ContinueFromBreakpointExecutesItAndRunsToEnd()
    {
        var (m, p) = Load(Program);
        var breakpoints = new HashSet<uint> { p.Symbols["middle"] };
        m.RunUntil(1000, breakpoints);
        var reason = m.RunUntil(1000, breakpoints);
        Assert.Equal(StopReason.Halted, reason);
        Assert.Equal(4, m.Registers[11]);
    }

    [Fact]
    public void BreakpointInsideLoopStopsEveryIteration()
    {
        var (m, p) = Load(".text\nmain:\nli $t0, 0\nloop:\naddi $t0, $t0, 1\nblt $t0, 3, loop\nli $v0, 10\nsyscall");
        var breakpoints = new HashSet<uint> { p.Symbols["loop"] };
        Assert.Equal(StopReason.Breakpoint, m.RunUntil(1000, breakpoints));
        Assert.Equal(0, m.Registers[8]);
        Assert.Equal(StopReason.Breakpoint, m.RunUntil(1000, breakpoints));
        Assert.Equal(1, m.Registers[8]);
        Assert.Equal(StopReason.Breakpoint, m.RunUntil(1000, breakpoints));
        Assert.Equal(2, m.Registers[8]);
        Assert.Equal(StopReason.Halted, m.RunUntil(1000, breakpoints));
        Assert.Equal(3, m.Registers[8]);
    }

    [Fact]
    public void RunToAddressStopsThere()
    {
        var (m, p) = Load(Program);
        var reason = m.RunUntil(1000, runToAddress: p.Symbols["end"]);
        Assert.Equal(StopReason.RunToTarget, reason);
        Assert.Equal(p.Symbols["end"], m.PC);
        Assert.Equal(3, m.Registers[10]);
        Assert.Equal(0, m.Registers[11]);
    }

    [Fact]
    public void CancelledRunPausesWithoutExecuting()
    {
        var (m, _) = Load(Program);
        var start = m.PC;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Equal(StopReason.Paused, m.RunUntil(1000, cancellation: cts.Token));
        Assert.Equal(start, m.PC);
        Assert.False(m.Halted);
    }

    [Fact]
    public void InfiniteLoopReachesLimitWithoutHalting()
    {
        var (m, _) = Load(".text\nmain:\nloop:\nj loop");
        Assert.Equal(StopReason.LimitReached, m.RunUntil(100));
        Assert.False(m.Halted);
    }

    [Fact]
    public void StepBackWorksAfterStoppingAtBreakpoint()
    {
        var (m, p) = Load(Program);
        m.RunUntil(1000, new HashSet<uint> { p.Symbols["end"] });
        Assert.Equal(3, m.Registers[10]);
        Assert.True(m.StepBack());
        Assert.Equal(0, m.Registers[10]);
    }

    [Fact]
    public void RuntimeApiRunUntilStopsAtBreakpoint()
    {
        var runtime = new ASMForgeRuntime();
        var program = runtime.LoadAssembly(Program);
        var reason = runtime.RunUntil(new HashSet<uint> { program.Symbols["middle"] });
        Assert.Equal(StopReason.Breakpoint, reason);
        Assert.Equal(1, runtime.Registers["$t0"]);
        Assert.False(runtime.IsRunning);
    }
}
