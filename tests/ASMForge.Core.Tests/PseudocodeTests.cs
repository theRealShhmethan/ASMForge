using System.Text;
using ASMForge.Core.Assembly;
using ASMForge.Core.Execution;
using ASMForge.Core.Pseudocode;
using Xunit;

namespace ASMForge.Core.Tests;

/// <summary>Pseudocode generator: generate, check the assembly is clean, run it, and compare the output.</summary>
public class PseudocodeTests
{
    private static string Run(string pseudocode, string input = "")
    {
        var result = PseudocodeCompiler.Compile(pseudocode);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Empty(new SimpleAssembler().CheckAll(result.Assembly)); // generated code assembles cleanly
        var machine = new MipsMachine();
        machine.Load(new SimpleAssembler().Assemble(result.Assembly));
        if (input.Length > 0) machine.ProvideInput(input);
        machine.Run(5_000_000);
        return machine.ConsoleText;
    }

    private static PseudocodeError[] Errors(string pseudocode) => PseudocodeCompiler.Compile(pseudocode).Errors.ToArray();

    [Fact]
    public void UsersFizzBuzzRunsAsWrittenWithHashComments()
    {
        const string source = """
            # int i = 1;
            #
            # while (i <= 100) {
            #     if(i % 3 ==0 && i % 5 == 0) {print("FizzBuzz\n"); i++; continue;}
            #     if(i % 3 == 0) {print("Fizz\n"); i++; continue;}
            #     if(i % 5 == 0) {print("Buzz\n"); i++; continue;}
            #
            #     print(i);
            #     print("\n");
            #     i++;
            # }
            """;
        var expected = new StringBuilder();
        for (var i = 1; i <= 100; i++)
            expected.Append(i % 15 == 0 ? "FizzBuzz\n" : i % 3 == 0 ? "Fizz\n" : i % 5 == 0 ? "Buzz\n" : $"{i}\n");
        Assert.Equal(expected.ToString(), Run(source));
    }

    [Fact]
    public void GeneratedAssemblyFollowsHandWrittenStyle()
    {
        const string source = """
            int i = 1;
            while (i <= 100) {
                print(i, "\n");
                i++;
            }
            """;
        var assembly = PseudocodeCompiler.Compile(source).Assembly;
        Assert.Contains(".globl main", assembly);
        Assert.Contains("# while (i <= 100) {", assembly);          // the pseudocode as one comment block
        Assert.Contains("# Registers: $s0 = i", assembly);
        Assert.Contains("newline: .asciiz \"\\n\"", assembly);       // strings named after their text
        Assert.Matches(@"li \$s0, 1\s+# int i = 1", assembly);
        Assert.Matches(@"loop:\s+# while \(i <= 100\)", assembly);
        Assert.Matches(@"bgt \$s0, 100, endLoop\s+# if i > 100 → endLoop", assembly);
        Assert.Matches(@"addi \$s0, \$s0, 1\s+# i\+\+", assembly);
        Assert.Matches(@"syscall\s+# Print i", assembly);
        Assert.Matches(@"syscall\s+# Exit program", assembly);
    }

    [Fact]
    public void FizzBuzzStringsAreNamedLikeHandWrittenCode()
    {
        var assembly = PseudocodeCompiler.Compile("""print("FizzBuzz\n", "Fizz\n", "Buzz\n", "\n");""").Assembly;
        Assert.Contains("fizzBuzz: .asciiz", assembly);
        Assert.Contains("fizz: .asciiz", assembly);
        Assert.Contains("buzz: .asciiz", assembly);
        Assert.Contains("newline: .asciiz", assembly);
    }

    [Fact]
    public void VariablesBeyondEightSavedRegistersLiveInMemory()
    {
        const string source = "int a = 1, b = 2, c = 3, d = 4, e = 5, f = 6, g = 7, h = 8, i = 9, j = 10;\nprint(a + b + c + d + e + f + g + h + i + j);";
        Assert.Contains("# In memory (.data): i, j", PseudocodeCompiler.Compile(source).Assembly);
        Assert.Equal("55", Run(source));
    }

    [Fact]
    public void ArithmeticFollowsCPrecedence()
    {
        Assert.Equal("13 20 2 1 -7", Run("""
            int a = 7; int b = 3;
            print(a + b * 2, " ", (a + b) * 2, " ", a / b, " ", a % b, " ", -a);
            """));
    }

    [Fact]
    public void AssignmentsCanUseTheVariableOnBothSides()
    {
        Assert.Equal("12 2", Run("int x = 3;\nint y = 2;\nx = x * (x + 1);\ny = x - y * 5;\nprint(x, \" \", y);"));
    }

    [Fact]
    public void ComparisonsAndNotProduceZeroOrOne()
    {
        Assert.Equal("11000", Run("int a = 5;\nprint(a > 3, a == 5, a != 5, a <= 4, !a);"));
    }

    [Fact]
    public void AndOrShortCircuit()
    {
        // 10 / x would divide by zero if the right side were evaluated.
        Assert.Equal("safe ok", Run("""
            int x = 0;
            if (x != 0 && 10 / x > 1) { print("bad"); } else { print("safe"); }
            if (x == 0 || 10 / x > 1) print(" ok");
            """));
    }

    [Fact]
    public void ForLoopsAndArrays()
    {
        Assert.Equal("30", Run("""
            int a[5];
            for (int i = 0; i < 5; i++) { a[i] = i * i; }
            int sum = 0;
            for (int i = 0; i < 5; i++) sum += a[i];
            print(sum);
            """));
    }

    [Fact]
    public void ArrayInitializers()
    {
        Assert.Equal("7", Run("int a[4] = {3, 1, 4, 1};\nprint(a[0] + a[2]);"));
    }

    [Fact]
    public void DoWhileWithBreakAndContinue()
    {
        Assert.Equal("134", Run("int i = 0;\ndo {\n  i++;\n  if (i == 2) continue;\n  if (i == 5) break;\n  print(i);\n} while (i < 10);"));
    }

    [Fact]
    public void ElseIfChains()
    {
        Assert.Equal("B", Run("""
            int score = 85;
            if (score >= 90) print("A");
            else if (score >= 80) print("B");
            else print("C");
            """));
    }

    [Fact]
    public void ReadsInputAndPrintsCharacters()
    {
        const string source = """
            int n = readInt();
            int total = 0;
            for (int i = 1; i <= n; i++) total += i;
            int c = readChar();
            print(total, " ");
            printChar(c);
            print('!');
            """;
        Assert.EndsWith("10 x!", Run(source, "4\nx\n"));
    }

    [Fact]
    public void PrintingAReadValueKeepsTheSyscallOrderCorrect()
    {
        Assert.EndsWith("14", Run("print(readInt() * 2);", "7\n"));
    }

    [Fact]
    public void ArrayNamedLikeALabelIsRenamed()
    {
        const string source = "int x = 0;\nwhile (x < 1) { x++; }\nint loop[2] = {3, 4};\nint main[1] = {5};\nprint(loop[0] + loop[1] + main[0]);";
        var assembly = PseudocodeCompiler.Compile(source).Assembly;
        Assert.Contains("var_loop: .word 3, 4", assembly);
        Assert.Contains("var_main: .word 5", assembly);
        Assert.Equal("12", Run(source));
    }

    [Fact]
    public void UndeclaredVariableReportsLineAndColumn()
    {
        var error = Assert.Single(Errors("int a = 1;\nb = 2;"));
        Assert.Equal((2, 1), (error.Line, error.Column));
        Assert.Contains("'b' is not declared", error.Message);
    }

    [Fact]
    public void MissingSemicolonIsReportedWhereItBelongs()
    {
        var error = Assert.Single(Errors("int a = 1\nprint(a);"));
        Assert.Equal((1, 10), (error.Line, error.Column));
        Assert.Contains("Missing ';'", error.Message);
    }

    [Fact]
    public void SeveralErrorsAreReportedAtOnce()
    {
        Assert.Equal(new[] { 1, 2 }, Errors("int a = 1\nint b = 2").Select(e => e.Line).ToArray());
    }

    [Theory]
    [InlineData("break;", "'break' can only be used inside a loop")]
    [InlineData("foo(1);", "Unknown function 'foo'")]
    [InlineData("int x = 0;\nif (x = 1) print(x);", "Use '=='")]
    [InlineData("int x = \"hi\";", "can only be used inside print")]
    [InlineData("int x = 1;\nint y = x++;", "can only be used as its own statement")]
    [InlineData("int a[3] = {1, 2, 3, 4};", "has room for 3 values")]
    [InlineData("int a = 1;\nprint(a[0]);", "is not an array")]
    public void HelpfulErrorMessages(string source, string expected)
    {
        Assert.Contains(Errors(source), e => e.Message.Contains(expected, StringComparison.Ordinal));
    }
}
