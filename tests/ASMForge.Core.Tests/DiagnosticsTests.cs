using ASMForge.Core.Assembly;
using Xunit;

namespace ASMForge.Core.Tests;

/// <summary>CheckAll powers the editor's live red squiggles: it must report every bad line, without cascades.</summary>
public class DiagnosticsTests
{
    private static int[] ErrorLines(string source) =>
        new SimpleAssembler().CheckAll(source).Select(e => e.Line).ToArray();

    [Fact]
    public void ValidProgramHasNoErrors()
    {
        Assert.Empty(new SimpleAssembler().CheckAll(".data\nx: .word 1\n.text\nmain:\nlw $t0, x\nli $v0, 10\nsyscall"));
    }

    [Fact]
    public void ReportsEveryBadLine()
    {
        var source = "add $t0, $t1\nli $t0, 5\nfoo $t1, $t2\nsll $t0, $t0, 40\nbeq $t0, $t1, nowhere";
        Assert.Equal(new[] { 1, 3, 4, 5 }, ErrorLines(source));
    }

    [Fact]
    public void LabelsOnBadLinesStillResolve()
    {
        // "ad" is a typo, but "loop" must still exist so "j loop" is not reported as an undefined label.
        Assert.Equal(new[] { 1 }, ErrorLines("loop: ad $t0, $t0, 1\nj loop"));
    }

    [Fact]
    public void MessagesDoNotRepeatTheLinePrefix()
    {
        var error = Assert.Single(new SimpleAssembler().CheckAll("li $t0, 1\nfoo"));
        Assert.Equal(2, error.Line);
        Assert.StartsWith("unknown instruction 'foo'", error.Message);
    }

    [Fact]
    public void DuplicateLabelsAreReportedWithoutLooping()
    {
        Assert.Equal(new[] { 2 }, ErrorLines("a: nop\na: nop\nj a"));
    }

    [Fact]
    public void DirectiveMissingItsDotSuggestsTheDirective()
    {
        var error = Assert.Single(new SimpleAssembler().CheckAll(".data\nmsg: .asciiz \"hi\"\n\ntext\nmain:\nli $v0, 10\nsyscall"));
        Assert.Equal(4, error.Line);
        Assert.StartsWith("unknown instruction 'text'", error.Message);
        Assert.Contains("Did you mean '.text'?", error.Message);
    }

    [Fact]
    public void SectionErrorsAreReported()
    {
        Assert.Equal(new[] { 2, 4 }, ErrorLines(".data\nadd $t0, $t1, $t2\n.text\n.word 5"));
    }
}
