using ASMForge.Core.Assembly;
using ASMForge.Core.Cpu;
using ASMForge.Core.Execution;
using ASMForge.Core.Memory;
using Xunit;

namespace ASMForge.Core.Tests;

/// <summary>Instruction semantics regression tests (roadmap Phase 1.3).</summary>
public class InstructionTests
{
    private static MipsMachine Run(string source)
    {
        var machine = new MipsMachine();
        machine.Load(new SimpleAssembler().Assemble(source));
        machine.Run();
        return machine;
    }

    private static int R(MipsMachine m, string register) => m.Registers[RegisterFile.Parse(register)];

    [Fact]
    public void SignedAddOverflowTraps()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Run("li $t0, 0x7FFFFFFF\naddi $t1, $t0, 1"));
        Assert.Contains("overflow", error.Message);
    }

    [Fact]
    public void UnsignedAddWrapsAround()
    {
        Assert.Equal(int.MinValue, R(Run("li $t0, 0x7FFFFFFF\naddiu $t1, $t0, 1"), "$t1"));
    }

    [Fact]
    public void SubtractionSignedAndUnsigned()
    {
        var m = Run("li $t0, 5\nli $t1, 7\nsub $t2, $t0, $t1\nsubu $t3, $t0, $t1");
        Assert.Equal(-2, R(m, "$t2"));
        Assert.Equal(-2, R(m, "$t3"));
    }

    [Fact]
    public void MultiplicationSetsHiAndLo()
    {
        var signed = Run("li $t0, -3\nli $t1, 0x40000000\nmult $t0, $t1");
        Assert.Equal(0x40000000, signed.Registers.LO);
        Assert.Equal(-1, signed.Registers.HI);

        var unsigned = Run("li $t0, -1\nli $t1, 2\nmultu $t0, $t1");
        Assert.Equal(-2, unsigned.Registers.LO);
        Assert.Equal(1, unsigned.Registers.HI);
    }

    [Fact]
    public void DivisionSetsQuotientAndRemainder()
    {
        var signed = Run("li $t0, -7\nli $t1, 2\ndiv $t0, $t1\nmflo $s0\nmfhi $s1");
        Assert.Equal(-3, R(signed, "$s0"));
        Assert.Equal(-1, R(signed, "$s1"));

        var unsigned = Run("li $t0, -1\nli $t1, 2\ndivu $t0, $t1");
        Assert.Equal(0x7FFFFFFF, unsigned.Registers.LO);
        Assert.Equal(1, unsigned.Registers.HI);
    }

    [Fact]
    public void ShiftsLogicalAndArithmetic()
    {
        var m = Run("li $t0, -16\nsll $t1, $t0, 4\nsrl $t2, $t0, 4\nsra $t3, $t0, 4\nli $t4, 3\nsllv $t5, $t0, $t4\nsrav $t6, $t0, $t4");
        Assert.Equal(-256, R(m, "$t1"));
        Assert.Equal(0x0FFFFFFF, R(m, "$t2"));
        Assert.Equal(-1, R(m, "$t3"));
        Assert.Equal(-128, R(m, "$t5"));
        Assert.Equal(-2, R(m, "$t6"));
    }

    [Fact]
    public void ComparisonsSignedAndUnsigned()
    {
        var m = Run("li $t0, -1\nli $t1, 1\nslt $s0, $t0, $t1\nsltu $s1, $t0, $t1\nslti $s2, $t0, 0\nsltiu $s3, $t1, -1");
        Assert.Equal(1, R(m, "$s0"));
        Assert.Equal(0, R(m, "$s1")); // 0xFFFFFFFF is not below 1 unsigned
        Assert.Equal(1, R(m, "$s2"));
        Assert.Equal(1, R(m, "$s3")); // sltiu sign-extends -1 to 0xFFFFFFFF, then compares unsigned
    }

    [Fact]
    public void LoadsSignAndZeroExtend()
    {
        var m = Run(".data\nb: .byte -1\nh: .half -2\n.text\nmain:\nlb $t0, b\nlbu $t1, b\nlh $t2, h\nlhu $t3, h");
        Assert.Equal(-1, R(m, "$t0"));
        Assert.Equal(255, R(m, "$t1"));
        Assert.Equal(-2, R(m, "$t2"));
        Assert.Equal(65534, R(m, "$t3"));
    }

    [Fact]
    public void StoresWriteWordsHalvesAndBytes()
    {
        var m = Run(".data\nbuf: .space 8\n.text\nmain:\nli $t0, 0x11223344\nsw $t0, buf\nsb $t0, buf+4\nsh $t0, buf+6");
        Assert.Equal(0x11223344u, m.Memory.ReadWordUnsigned(0x10010000));
        Assert.Equal(0x44, m.Memory.ReadByte(0x10010004));
        Assert.Equal(0x3344, m.Memory.ReadHalf(0x10010006));
    }

    [Fact]
    public void UnalignedWordAccessIsAnError()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Run(".data\nx: .word 1\n.text\nmain:\nla $t0, x\nlw $t1, 2($t0)"));
        Assert.Contains("Unaligned", error.Message);
    }

    [Fact]
    public void JalAndJrReturnToTheCaller()
    {
        var m = Run("main:\njal f\nli $s0, 1\nli $v0, 10\nsyscall\nf:\nli $t0, 7\njr $ra");
        Assert.Equal(7, R(m, "$t0"));
        Assert.Equal(1, R(m, "$s0"));
    }

    [Fact]
    public void StackPushAndPop()
    {
        var m = Run("addi $sp, $sp, -8\nli $t0, 5\nsw $t0, 4($sp)\nlw $t1, 4($sp)\naddi $sp, $sp, 8");
        Assert.Equal(5, R(m, "$t1"));
        Assert.Equal(unchecked((int)MipsMemory.StackTop), R(m, "$sp"));
    }

    [Fact]
    public void HeapAllocationAdvancesTheBreak()
    {
        var m = Run("li $a0, 16\nli $v0, 9\nsyscall\nmove $t0, $v0\nli $a0, 8\nli $v0, 9\nsyscall\nmove $t1, $v0");
        Assert.Equal(unchecked((int)MipsMemory.HeapBase), R(m, "$t0"));
        Assert.Equal(unchecked((int)(MipsMemory.HeapBase + 16)), R(m, "$t1"));
    }

    [Fact]
    public void AsciizStringsAreNullTerminatedAndPrintable()
    {
        var m = Run(".data\ns: .asciiz \"Hi\"\n.text\nmain:\nla $a0, s\nli $v0, 4\nsyscall");
        Assert.Equal((byte)'H', m.Memory.ReadByte(0x10010000));
        Assert.Equal(0, m.Memory.ReadByte(0x10010002));
        Assert.Equal("Hi", m.ConsoleText);
    }

    [Fact]
    public void LoadResetsRegistersAndMemory()
    {
        var program = new SimpleAssembler().Assemble(".data\nx: .word 1\n.text\nmain:\nli $t0, 9\nsw $t0, x");
        var m = new MipsMachine();
        m.Load(program);
        m.Run();
        Assert.Equal(9, m.Memory.ReadWord(0x10010000));

        m.Load(program);
        Assert.Equal(1, m.Memory.ReadWord(0x10010000));
        Assert.Equal(0, R(m, "$t0"));
        Assert.Equal(unchecked((int)MipsMemory.StackTop), R(m, "$sp"));
        Assert.Equal(program.EntryPoint, m.PC);
    }

    [Fact]
    public void StepExecutesExactlyOneInstruction()
    {
        var m = new MipsMachine();
        m.Load(new SimpleAssembler().Assemble("li $t0, 1\nli $t1, 2"));
        m.Step();
        Assert.Equal(1, R(m, "$t0"));
        Assert.Equal(0, R(m, "$t1"));
        Assert.Equal(0x00400004u, m.PC);
    }
}
