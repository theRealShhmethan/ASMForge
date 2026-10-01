using ASMForge.Core.Assembly;
using ASMForge.Core.Execution;
using Xunit;

namespace ASMForge.Core.Tests;

/// <summary>Pseudo-instruction expansions and text addresses must match MARS 4.5 (PseudoOps.txt).</summary>
public class PseudoInstructionTests
{
    private static string[] Basic(string source) =>
        new SimpleAssembler().Assemble(source).Instructions.Select(i => i.BasicSource).ToArray();

    private static MipsMachine Run(string source)
    {
        var machine = new MipsMachine();
        machine.Load(new SimpleAssembler().Assemble(source));
        machine.Run();
        return machine;
    }

    [Fact]
    public void LiPicksSmallestMarsForm()
    {
        Assert.Equal(new[] { "addiu $t0, $zero, 5" }, Basic("li $t0, 5"));
        Assert.Equal(new[] { "addiu $t0, $zero, -1" }, Basic("li $t0, -1"));
        Assert.Equal(new[] { "addiu $t0, $zero, -1" }, Basic("li $t0, 0xFFFFFFFF"));
        Assert.Equal(new[] { "ori $t0, $zero, 40000" }, Basic("li $t0, 40000"));
        Assert.Equal(new[] { "lui $at, 1", "ori $t0, $at, 34464" }, Basic("li $t0, 100000"));
    }

    [Fact]
    public void LaAndLabelLoadsUseAt()
    {
        var source = ".data\nvalue: .word 5\n.text\nmain:\nla $a0, value\nlw $t0, value\nsw $t0, value";
        Assert.Equal(new[]
        {
            "lui $at, 4097", "ori $a0, $at, 0",
            "lui $at, 4097", "lw $t0, 0($at)",
            "lui $at, 4097", "sw $t0, 0($at)"
        }, Basic(source));
    }

    [Fact]
    public void LabelLoadAdjustsHighHalfWhenBit15IsSet()
    {
        var source = ".data\npad: .space 32768\nx: .word 77\n.text\nmain:\nlw $t0, x";
        Assert.Equal(new[] { "lui $at, 4098", "lw $t0, -32768($at)" }, Basic(source));
        Assert.Equal(77, Run(source).Registers[8]);
    }

    [Fact]
    public void BranchPseudoInstructionsExpandLikeMars()
    {
        Assert.Equal(new[] { "slt $at, $t1, $t0", "bne $at, $zero, done" }, Basic("bgt $t0, $t1, done\ndone:"));
        Assert.Equal(new[] { "slti $at, $t0, 5", "bne $at, $zero, done" }, Basic("blt $t0, 5, done\ndone:"));
        Assert.Equal(new[] { "bgez $zero, done" }, Basic("b done\ndone:"));
        Assert.Equal(new[] { "addi $at, $zero, 1", "beq $at, $t0, done" }, Basic("beq $t0, 1, done\ndone:"));
    }

    [Fact]
    public void ArithmeticPseudoInstructionsExpandLikeMars()
    {
        Assert.Equal(new[] { "addu $t0, $zero, $t1" }, Basic("move $t0, $t1"));
        Assert.Equal(new[] { "nor $t0, $t1, $zero" }, Basic("not $t0, $t1"));
        Assert.Equal(new[] { "bne $t2, $zero, 1", "break", "div $t1, $t2", "mflo $t0" }, Basic("div $t0, $t1, $t2"));
        Assert.Equal(new[] { "addi $at, $zero, 4", "div $t1, $at", "mfhi $t0" }, Basic("rem $t0, $t1, 4"));
    }

    [Fact]
    public void NumberedRegistersAreShownByName()
    {
        Assert.Equal(new[] { "add $t0, $t1, $t2" }, Basic("add $8, $9, $10"));
    }

    [Fact]
    public void TextAddressesCountExpandedInstructions()
    {
        var program = new SimpleAssembler().Assemble("main:\nbgt $t0, $t1, next\nnext:\nli $t2, 1");
        Assert.Equal(0x00400008u, program.Symbols["next"]);
    }

    [Fact]
    public void JalReturnAddressAccountsForExpansions()
    {
        var source = "main:\nli $t0, 5\nbgt $t0, $zero, skip\nskip:\njal f\nli $v0, 10\nsyscall\nf:\nmove $t9, $ra\njr $ra";
        // li (1) + bgt (2) puts jal at 0x0040000C, so $ra = 0x00400010.
        Assert.Equal(0x00400010, Run(source).Registers[25]);
    }

    [Fact]
    public void ThreeOperandDivisionComputesQuotientAndRemainder()
    {
        var m = Run("li $t1, 17\nli $t2, 5\ndiv $t0, $t1, $t2\nrem $t3, $t1, $t2\ndivu $t4, $t1, $t2\nremu $t5, $t1, 3");
        Assert.Equal(3, m.Registers[8]);
        Assert.Equal(2, m.Registers[11]);
        Assert.Equal(3, m.Registers[12]);
        Assert.Equal(2, m.Registers[13]);
    }

    [Fact]
    public void DivisionByZeroInPseudoDivBreaks()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Run("li $t1, 5\ndiv $t0, $t1, $zero"));
        Assert.Contains("Division by zero", error.Message);
    }

    [Fact]
    public void ComparisonPseudoInstructionsProduceCorrectValues()
    {
        var m = Run("li $t1, 3\nli $t2, 3\nli $t3, -5\n" +
                    "sge $s0, $t1, $t2\nsgt $s1, $t1, $t2\nseq $s2, $t1, $t2\nsne $s3, $t1, $t2\n" +
                    "sle $s4, $t3, $t1\nabs $s5, $t3\nneg $s6, $t1");
        Assert.Equal(1, m.Registers[16]);
        Assert.Equal(0, m.Registers[17]);
        Assert.Equal(1, m.Registers[18]);
        Assert.Equal(0, m.Registers[19]);
        Assert.Equal(1, m.Registers[20]);
        Assert.Equal(5, m.Registers[21]);
        Assert.Equal(-3, m.Registers[22]);
    }

    [Fact]
    public void BranchPseudoInstructionsBranchCorrectly()
    {
        var m = Run("li $t0, 7\nli $s0, 0\n" +
                    "bgt $t0, 5, l1\nli $s0, 99\nl1:\n" +
                    "ble $t0, 7, l2\nli $s0, 99\nl2:\n" +
                    "bge $t0, 100000, l3\naddi $s0, $s0, 1\nl3:\n" +
                    "bltu $t0, $zero, l4\naddi $s0, $s0, 1\nl4:");
        Assert.Equal(2, m.Registers[16]);
    }

    [Fact]
    public void WrongOperandCountListsAcceptedForms()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Basic("add $t0, $t1"));
        Assert.Contains("invalid operands for 'add'", error.Message);
        Assert.Contains("add $t1,$t2,$t3", error.Message);
    }

    [Fact]
    public void OutOfRangeShiftIsReported()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Basic("sll $t0, $t1, 40"));
        Assert.Contains("out of range", error.Message);
    }

    [Fact]
    public void LiWithLabelSuggestsLa()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Basic(".data\nx: .word 1\n.text\nli $t0, x"));
        Assert.Contains("Use 'la'", error.Message);
    }

    [Fact]
    public void UndefinedBranchLabelIsReported()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Basic("beq $t0, $t1, nowhere"));
        Assert.Contains("undefined label 'nowhere'", error.Message);
    }

    [Fact]
    public void InstructionInDataSectionIsReported()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Basic(".data\nadd $t0, $t1, $t2"));
        Assert.Contains(".data section", error.Message);
    }

    [Fact]
    public void DataDirectiveInTextSectionIsReported()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Basic(".text\n.word 5"));
        Assert.Contains(".text section", error.Message);
    }

    [Fact]
    public void EqvConstantsWorkAsImmediates()
    {
        Assert.Equal(new[] { "lui $at, 1", "ori $t0, $at, 34464" }, Basic(".eqv SIZE 100000\nli $t0, SIZE"));
    }

    [Fact]
    public void CharacterLiteralsWorkAsImmediates()
    {
        Assert.Equal(new[] { "addiu $a0, $zero, 65" }, Basic("li $a0, 'A'"));
    }
}
