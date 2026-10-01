using ASMForge.Core.Assembly;
using Xunit;

namespace ASMForge.Core.Tests;

/// <summary>Machine words must match the standard MIPS32 encodings (as shown by MARS).</summary>
public class MachineCodeTests
{
    private static uint[] Codes(string source) =>
        new SimpleAssembler().Assemble(source).Instructions.Select(i => i.MachineCode).ToArray();

    private static uint Code(string source) => Assert.Single(Codes(source));

    [Theory]
    [InlineData("add $t2, $t0, $t1", 0x01095020u)]
    [InlineData("sub $t2, $t0, $t1", 0x01095022u)]
    [InlineData("and $t2, $t0, $t1", 0x01095024u)]
    [InlineData("slt $t2, $t0, $t1", 0x0109502Au)]
    [InlineData("sllv $t0, $t1, $t2", 0x01494004u)]
    [InlineData("sll $t0, $t1, 2", 0x00094080u)]
    [InlineData("sra $t0, $t1, 31", 0x000947C3u)]
    [InlineData("mult $t0, $t1", 0x01090018u)]
    [InlineData("div $t0, $t1", 0x0109001Au)]
    [InlineData("mflo $t2", 0x00005012u)]
    [InlineData("mthi $t0", 0x01000011u)]
    [InlineData("jr $ra", 0x03E00008u)]
    [InlineData("jalr $t9", 0x0320F809u)]
    [InlineData("syscall", 0x0000000Cu)]
    [InlineData("break", 0x0000000Du)]
    [InlineData("nop", 0x00000000u)]
    [InlineData("mul $t0, $t1, $t2", 0x712A4002u)]
    [InlineData("madd $t0, $t1", 0x71090000u)]
    public void RTypeAndSpecial2(string source, uint expected) => Assert.Equal(expected, Code(source));

    [Theory]
    [InlineData("lui $at, 4097", 0x3C011001u)]
    [InlineData("ori $a0, $at, 0", 0x34240000u)]
    [InlineData("addi $sp, $sp, -8", 0x23BDFFF8u)]
    [InlineData("addiu $t0, $zero, 5", 0x24080005u)]
    [InlineData("andi $t0, $t1, 65535", 0x3128FFFFu)]
    [InlineData("slti $at, $t0, 5", 0x29010005u)]
    [InlineData("lw $t0, 0($at)", 0x8C280000u)]
    [InlineData("sw $ra, 4($sp)", 0xAFBF0004u)]
    [InlineData("lb $t0, -1($a0)", 0x8088FFFFu)]
    [InlineData("sh $t1, 2($t2)", 0xA5490002u)]
    public void IType(string source, uint expected) => Assert.Equal(expected, Code(source));

    [Fact]
    public void BranchOffsetsAreRelativeToNextInstruction()
    {
        // loop is at 0x00400000; the beq at 0x00400004 branches back: offset = (0x00400000 - 0x00400008) / 4 = -2.
        Assert.Equal(0x1109FFFEu, Codes("loop:\nnop\nbeq $t0, $t1, loop")[1]);
        // Forward: bne at 0x00400000 to 0x00400008 -> offset 1.
        Assert.Equal(0x15090001u, Codes("bne $t0, $t1, skip\nnop\nskip:\nnop")[0]);
        // b (bgez $zero) to the very next instruction -> offset 0.
        Assert.Equal(0x04010000u, Codes("b next\nnext:\nnop")[0]);
        Assert.Equal(0x1D000001u, Codes("bgtz $t0, skip\nnop\nskip:\nnop")[0]);
        Assert.Equal(0x05110001u, Codes("bgezal $t0, skip\nnop\nskip:\nnop")[0]);
    }

    [Fact]
    public void JumpsEncodeWordTarget()
    {
        var codes = Codes("main:\nj main\njal f\nnop\nf:\njr $ra");
        Assert.Equal(0x08100000u, codes[0]); // 0x00400000 >> 2
        Assert.Equal(0x0C100003u, codes[1]); // f = 0x0040000C
    }

    [Fact]
    public void PseudoInstructionExpansionsAreEncoded()
    {
        // la $a0, msg (msg = 0x10010000) and the generated div zero-check branch.
        Assert.Equal(new[] { 0x3C011001u, 0x34240000u }, Codes(".data\nmsg: .asciiz \"hi\"\n.text\nla $a0, msg"));
        var div = Codes("div $t0, $t1, $t2");
        Assert.Equal(0x15400001u, div[0]); // bne $t2, $zero, 1
        Assert.Equal(0x0000000Du, div[1]); // break
        Assert.Equal(0x012A001Au, div[2]); // div $t1, $t2
        Assert.Equal(0x00004012u, div[3]); // mflo $t0
    }

    [Fact]
    public void DescribeShowsFields()
    {
        var text = InstructionEncoder.Describe(0x01095020);
        Assert.Contains("R-type", text);
        Assert.Contains("rd      01010   ($t2)", text);
        Assert.Contains("funct   100000  (0x20)", text);
        Assert.Contains("000000 01000 01001 01010 00000 100000", text);

        Assert.Contains("I-type", InstructionEncoder.Describe(0x23BDFFF8));
        Assert.Contains("(-8, 0xFFF8)", InstructionEncoder.Describe(0x23BDFFF8));
        Assert.Contains("J-type", InstructionEncoder.Describe(0x08100000));
        Assert.Contains("address 0x00400000", InstructionEncoder.Describe(0x08100000));
    }
}
