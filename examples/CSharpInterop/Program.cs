using ASMForge.Core.Execution;

var source = """
.data
message: .asciiz "Hello from C# through ASMForge MIPS!\n"
value:   .word 41

.text
main:
    la $a0, message
    li $v0, 4
    syscall
    lw $t0, value
    addi $t0, $t0, 1
    li $v0, 10
    syscall
""";

var mips = new ASMForgeRuntime();
mips.LoadAssembly(source);
mips.Run();

Console.Write(mips.Output);
var t0 = mips.Registers["$t0"];
var valueAddress = mips.Program!.Symbols["value"];
Console.WriteLine($"$t0 = {t0}");
Console.WriteLine($"value memory = {mips.Memory.ReadWord(valueAddress)}");
