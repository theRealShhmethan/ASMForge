using ASMForge.Core.Assembly;
using ASMForge.Core.Execution;
using Xunit;

namespace ASMForge.Core.Tests;

public class MachineTests
{
    [Fact]
    public void AddsRegisters()
    {
        var p = new SimpleAssembler().Assemble("li $t0, 5\nli $t1, 6\nadd $t2, $t0, $t1");
        var m = new MipsMachine(); m.Load(p); m.Run();
        Assert.Equal(11, m.Registers[10]);
    }

    [Fact]
    public void ZeroRegisterCannotChange()
    {
        var p = new SimpleAssembler().Assemble("li $zero, 99");
        var m = new MipsMachine(); m.Load(p); m.Run();
        Assert.Equal(0, m.Registers[0]);
    }

    [Fact]
    public void DataLabelAndPrintStringWork()
    {
        var source = ".data\nmsg: .asciiz \"Hello\\n\"\n.text\nmain:\nla $a0, msg\nli $v0, 4\nsyscall\nli $v0, 10\nsyscall";
        var p = new SimpleAssembler().Assemble(source);
        var m = new MipsMachine(); m.Load(p); m.Run();
        Assert.Equal("Hello\n", m.ConsoleText);
        Assert.Equal(0x10010000u, p.Symbols["msg"]);
    }

    [Fact]
    public void WordDataCanBeLoadedByLabel()
    {
        var source = ".data\nvalue: .word 123456\n.text\nmain:\nlw $t0, value\n";
        var p = new SimpleAssembler().Assemble(source);
        var m = new MipsMachine(); m.Load(p); m.Run();
        Assert.Equal(123456, m.Registers[8]);
    }

    [Fact]
    public void BranchUsesRealTextAddress()
    {
        var source = ".text\nmain:\nli $t0, 1\nbeq $t0, 1, yes\nli $t1, 99\nyes:\nli $t1, 7";
        var p = new SimpleAssembler().Assemble(source);
        var m = new MipsMachine(); m.Load(p); m.Run();
        Assert.Equal(7, m.Registers[9]);
        Assert.True(p.Symbols["yes"] >= 0x00400000u);
    }

    [Fact]
    public void NumericDataLabelsUseMarsAutoAlignment()
    {
        var source = ".data\nb: .byte 65\nh: .half 1234\nw: .word 0x12345678\n.text\nmain:\nlh $t0, h\nlw $t1, w";
        var p = new SimpleAssembler().Assemble(source);
        var m = new MipsMachine(); m.Load(p); m.Run();
        Assert.Equal(0x10010002u, p.Symbols["h"]);
        Assert.Equal(0x10010004u, p.Symbols["w"]);
        Assert.Equal(1234, m.Registers[8]);
        Assert.Equal(unchecked((int)0x12345678), m.Registers[9]);
    }

    [Fact]
    public void AlignZeroDisablesAutomaticNumericAlignment()
    {
        var source = ".data\n.align 0\nb: .byte 1\nh: .half 0x1234";
        var p = new SimpleAssembler().Assemble(source);
        Assert.Equal(0x10010001u, p.Symbols["h"]);
    }

    [Fact]
    public void Syscall34UsesMarsHexFormatting()
    {
        var source = ".text\nmain:\nli $a0, 5\nli $v0, 34\nsyscall\nli $v0, 10\nsyscall";
        var p = new SimpleAssembler().Assemble(source);
        var m = new MipsMachine(); m.Load(p); m.Run();
        Assert.Equal("0x00000005", m.ConsoleText);
    }

    [Fact]
    public void CSharpRuntimeBridgeRunsAndExposesMachineState()
    {
        var runtime = new ASMForgeRuntime();
        runtime.LoadAssembly(".data\nvalue: .word 41\n.text\nmain:\nlw $t0, value\naddi $t0, $t0, 1\nli $v0, 10\nsyscall");
        runtime.Run();
        Assert.Equal(42, runtime.Registers["$t0"]);
        Assert.Equal(41, runtime.Memory.ReadWord(0x10010000));
        Assert.True(runtime.IsHalted);
    }
}
