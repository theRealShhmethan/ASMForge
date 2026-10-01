using ASMForge.Core.Assembly;
using ASMForge.Core.Execution;
using Xunit;

namespace ASMForge.Core.Tests;

public class SyscallTests
{
    private const string ReadIntProgram = "li $v0, 5\nsyscall\nmove $t0, $v0\nli $v0, 10\nsyscall";

    private static MipsMachine Load(string source)
    {
        var machine = new MipsMachine();
        machine.Load(new SimpleAssembler().Assemble(source));
        return machine;
    }

    [Fact]
    public void ReadIntegerWaitsForInputThenContinues()
    {
        var m = Load(ReadIntProgram);
        Assert.Equal(StopReason.WaitingForInput, m.RunUntil(1000));
        Assert.True(m.WaitingForInput);
        Assert.Equal(InputKind.Integer, m.PendingInputKind);
        Assert.Equal(0x00400004u, m.PC); // still on the syscall

        m.ProvideInput("42");
        Assert.Equal(StopReason.Halted, m.RunUntil(1000));
        Assert.Equal(42, m.Registers[8]);
        Assert.Equal("42\n", m.ConsoleText); // input is echoed like a terminal
    }

    [Fact]
    public void ReadStringFollowsFgetsSemantics()
    {
        const string program = ".data\nbuf: .space 8\n.text\nmain:\nla $a0, buf\nli $a1, 8\nli $v0, 8\nsyscall";
        var m = Load(program);
        m.ProvideInput("hi");
        m.Run();
        Assert.Equal("hi\n", m.Memory.ReadCString(0x10010000)); // newline kept when it fits

        var truncated = Load(program.Replace("li $a1, 8", "li $a1, 3"));
        truncated.ProvideInput("hello");
        truncated.Run();
        Assert.Equal("he", truncated.Memory.ReadCString(0x10010000)); // at most n-1 characters
    }

    [Fact]
    public void ReadCharacterReturnsFirstCharacter()
    {
        var m = Load("li $v0, 12\nsyscall");
        m.ProvideInput("xyz");
        m.Run();
        Assert.Equal((int)'x', m.Registers[2]);
    }

    [Fact]
    public void InvalidIntegerIsARuntimeErrorThatStepBackRecovers()
    {
        var m = Load(ReadIntProgram);
        m.ProvideInput("abc");
        var error = Assert.Throws<InvalidOperationException>(() => m.RunUntil(1000));
        Assert.Contains("Invalid integer input", error.Message);
        Assert.True(m.Halted);

        Assert.True(m.StepBack());
        Assert.False(m.Halted);
        Assert.Equal(string.Empty, m.UnreadInput);
        m.Step();
        Assert.True(m.WaitingForInput); // asks again
    }

    [Fact]
    public void StepBackOverReadDiscardsTheInputAndAsksAgain()
    {
        var m = Load(ReadIntProgram);
        m.ProvideInput("5");
        m.Run();
        Assert.Equal(5, m.Registers[8]);

        for (var i = 0; i < 4; i++) Assert.True(m.StepBack()); // exit syscall, li, move, read syscall
        Assert.Equal(string.Empty, m.ConsoleText);
        Assert.Equal(string.Empty, m.UnreadInput);

        m.Step();
        Assert.True(m.WaitingForInput);
    }

    [Fact]
    public void RunWithoutInputExplainsWhatIsMissing()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Load(ReadIntProgram).Run());
        Assert.Contains("waiting for console input", error.Message);
    }

    [Fact]
    public void RuntimeApiProvidesInput()
    {
        var runtime = new ASMForgeRuntime();
        runtime.LoadAssembly("li $v0, 5\nsyscall\nmove $t0, $v0\nli $v0, 5\nsyscall\nadd $t0, $t0, $v0\nli $v0, 10\nsyscall");
        runtime.ProvideInput("3\n4\n");
        runtime.Run();
        Assert.Equal(7, runtime.Registers["$t0"]);
    }

    [Fact]
    public void SeededRandomRangeIsDeterministicAndInRange()
    {
        const string program = "li $a0, 1\nli $a1, 1234\nli $v0, 40\nsyscall\nli $a0, 1\nli $a1, 10\nli $v0, 42\nsyscall";
        var first = Load(program);
        first.Run();
        var second = Load(program);
        second.Run();
        Assert.InRange(first.Registers[4], 0, 9);
        Assert.Equal(first.Registers[4], second.Registers[4]);
    }

    [Fact]
    public void SystemTimeIsMillisecondsSinceEpoch()
    {
        var m = Load("li $v0, 30\nsyscall");
        m.Run();
        var milliseconds = ((long)m.Registers[5] << 32) | (uint)m.Registers[4];
        Assert.InRange(milliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 60_000, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1);
    }

    [Fact]
    public void RegisterAndMemoryEditsAreUndoable()
    {
        var m = Load(".data\nv: .word 7\n.text\nmain:\nnop");
        m.EditRegister(8, 99);
        Assert.Equal(99, m.Registers[8]);
        Assert.True(m.LastStep!.IsEdit);

        m.EditMemoryWord(0x10010000, 0xDEADBEEF);
        Assert.Equal(0xDEADBEEFu, m.Memory.ReadWordUnsigned(0x10010000));

        Assert.True(m.StepBack());
        Assert.Equal(7, m.Memory.ReadWord(0x10010000));
        Assert.True(m.StepBack());
        Assert.Equal(0, m.Registers[8]);
        Assert.Throws<InvalidOperationException>(() => m.EditRegister(0, 1));
    }

    [Fact]
    public void UndoingAnEditAfterHaltKeepsTheProgramHalted()
    {
        var m = Load("li $t0, 1");
        m.Run();
        Assert.True(m.Halted);
        m.EditRegister(8, 50);
        Assert.True(m.StepBack());
        Assert.True(m.Halted);
        Assert.Equal(1, m.Registers[8]);
    }
}
