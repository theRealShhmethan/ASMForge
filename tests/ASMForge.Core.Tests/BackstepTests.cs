using ASMForge.Core.Assembly;
using ASMForge.Core.Cpu;
using ASMForge.Core.Execution;
using ASMForge.Core.Memory;
using Xunit;

namespace ASMForge.Core.Tests;

public class BackstepTests
{
    private static MipsMachine Load(string source)
    {
        var machine = new MipsMachine();
        machine.Load(new SimpleAssembler().Assemble(source));
        return machine;
    }

    private static int[] Snapshot(MipsMachine machine) =>
        Enumerable.Range(0, RegisterFile.Count + 2).Select(machine.Registers.GetByIndex).ToArray();

    [Fact]
    public void StepRecordsRegisterWriteWithOldAndNewValues()
    {
        var m = Load("li $t0, 5\nli $t0, 7");
        m.Step(); m.Step();
        Assert.NotNull(m.LastStep);
        var change = Assert.Single(m.LastStep!.RegisterChanges);
        Assert.Equal(new RegisterChange(8, 5, 7), change);
        Assert.Empty(m.LastStep.MemoryChanges);
        Assert.False(m.LastStep.Faulted);
    }

    [Fact]
    public void ZeroRegisterWritesAreNotRecorded()
    {
        var m = Load("li $zero, 9");
        m.Step();
        Assert.Empty(m.LastStep!.RegisterChanges);
    }

    [Fact]
    public void MultRecordsHiAndLo()
    {
        var m = Load("li $t0, 3\nli $t1, 4\nmult $t0, $t1");
        m.Run();
        Assert.Contains(new RegisterChange(RegisterFile.LoIndex, 0, 12), m.LastStep!.RegisterChanges);
        Assert.Contains(new RegisterChange(RegisterFile.HiIndex, 0, 0), m.LastStep.RegisterChanges);
    }

    [Fact]
    public void WordStoreRecordsEachByte()
    {
        var m = Load(".data\nv: .word 0x11223344\n.text\nmain:\nli $t0, 0x55667788\nsw $t0, v");
        m.Run(); // li expands to lui + ori, then sw
        var changes = m.LastStep!.MemoryChanges;
        Assert.Equal(4, changes.Count);
        Assert.Equal(new MemoryChange(0x10010000, 0x44, 0x88), changes[0]);
        Assert.Equal(new MemoryChange(0x10010003, 0x11, 0x55), changes[3]);
    }

    [Fact]
    public void StepBackRestoresRegistersMemoryPcAndHaltState()
    {
        var m = Load(".data\nv: .word 1\n.text\nmain:\nli $t0, 42\nsw $t0, v\nli $t0, 0");
        m.Step();
        var pcBeforeStore = m.PC;
        m.Step(); m.Step();
        Assert.True(m.Halted);

        Assert.True(m.StepBack());
        Assert.False(m.Halted);
        Assert.Equal(42, m.Registers[8]);
        Assert.True(m.LastStepWasUndo);

        Assert.True(m.StepBack());
        Assert.Equal(1, m.Memory.ReadWord(0x10010000));
        Assert.Equal(pcBeforeStore, m.PC);
    }

    [Fact]
    public void StepBackRestoresConsoleOutputAndHeapBreak()
    {
        var source = ".data\nmsg: .asciiz \"hi\"\n.text\nmain:\n" +
                     "li $a0, 16\nli $v0, 9\nsyscall\n" +   // sbrk
                     "la $a0, msg\nli $v0, 4\nsyscall\n" +  // print "hi"
                     "li $v0, 10\nsyscall";
        var m = Load(source);
        m.Run();
        Assert.Equal("hi", m.ConsoleText);

        m.StepBack(); m.StepBack(); // undo exit
        Assert.Equal("hi", m.ConsoleText);
        m.StepBack(); // undo print
        Assert.Equal(string.Empty, m.ConsoleText);

        for (var i = 0; i < 4; i++) m.StepBack(); // li $v0, la (2), sbrk syscall
        m.Step(); // sbrk again must return the original heap start
        Assert.Equal(unchecked((int)MipsMemory.HeapBase), m.Registers[2]);
    }

    [Fact]
    public void StepBackRecoversFromRuntimeFault()
    {
        var m = Load(".data\nv: .word 1\n.text\nmain:\nla $t0, v\naddi $t0, $t0, 1\nlw $t1, 0($t0)");
        m.Step(); m.Step(); m.Step(); // la (2), addi
        var faultPc = m.PC;

        Assert.Throws<InvalidOperationException>(() => m.Step()); // unaligned word load
        Assert.True(m.Halted);
        Assert.True(m.LastStep!.Faulted);

        Assert.True(m.StepBack());
        Assert.False(m.Halted);
        Assert.Equal(faultPc, m.PC);

        Assert.True(m.StepBack());
        Assert.Equal(0x10010000, m.Registers[8]);
    }

    [Fact]
    public void StepBackToStartRestoresInitialState()
    {
        var source = ".data\nbuf: .space 8\n.text\nmain:\n" +
                     "li $t0, -3\nli $t1, 7\nmult $t0, $t1\nmflo $t2\nsw $t2, buf\n" +
                     "addi $sp, $sp, -4\nsw $t1, 0($sp)\n" +
                     "li $a0, 65\nli $v0, 11\nsyscall\nli $v0, 10\nsyscall";
        var m = Load(source);
        var initialRegisters = Snapshot(m);
        var initialPc = m.PC;

        m.Run();
        while (m.StepBack()) { }

        Assert.Equal(initialRegisters, Snapshot(m));
        Assert.Equal(initialPc, m.PC);
        Assert.Equal(0, m.Memory.ReadWord(0x10010000));
        Assert.Equal(0, m.Memory.ReadWord(MipsMemory.StackTop - 4));
        Assert.Equal(string.Empty, m.ConsoleText);
        Assert.False(m.CanStepBack);
    }

    [Fact]
    public void HistoryIsBoundedByLimit()
    {
        var m = Load("li $t0, 1\nli $t0, 2\nli $t0, 3\nli $t0, 4\nli $t0, 5");
        m.HistoryLimit = 2;
        m.Run();
        Assert.Equal(2, m.HistoryCount);
        Assert.True(m.StepBack());
        Assert.True(m.StepBack());
        Assert.False(m.StepBack());
        Assert.Equal(3, m.Registers[8]);
    }

    [Fact]
    public void LoadClearsHistory()
    {
        var program = new SimpleAssembler().Assemble("li $t0, 1\nli $t0, 2");
        var m = new MipsMachine();
        m.Load(program);
        m.Step();
        m.Load(program);
        Assert.False(m.CanStepBack);
        Assert.Null(m.LastStep);
    }

    [Fact]
    public void RuntimeApiExposesStepBack()
    {
        var runtime = new ASMForgeRuntime();
        runtime.LoadAssembly("li $t0, 1\nli $t0, 2");
        runtime.Run();
        Assert.True(runtime.CanStepBack);
        Assert.True(runtime.StepBack());
        Assert.Equal(1, runtime.Registers["$t0"]);
        Assert.False(runtime.IsHalted);
    }
}
