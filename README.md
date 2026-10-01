# ASMForge v0.8.1

ASMForge v0.8.1 is a compatibility and workflow patch on top of v0.8.

## What changed in v0.8.1

### C# projects now run inside ASMForge
- Added in-process Roslyn compilation using `Microsoft.CodeAnalysis.CSharp`.
- Press **Run (F5)** on a `.cs` file to compile and execute it directly in ASMForge.
- C# programs automatically receive a reference to `ASMForge.Core`, so the generated `ASMForgeRuntime` template works without creating a separate Visual Studio project.
- `Console.Write` and `Console.WriteLine` output is captured into **Run I/O**.
- Compiler and runtime errors are shown in **Messages** with file/line/column information when available.
- All `.cs` files in an open ASMForge project are compiled together; loose `.cs` files run by themselves.

### Project Explorer fixes
- New ASMForge projects now create both `main.asm` and `Program.cs`.
- The new-file dialog defaults to the currently open project folder.
- Creating a new `.asm` or `.cs` file refreshes Explorer immediately.
- `Program.cs` and other C# files under the project folder are shown in Explorer and included in C# compilation.

### Memory Viewer improvements
- Memory columns are user-resizable.
- Wider minimum/default column widths prevent normal 32-bit values from being clipped.
- Added memory value display modes:
  - Hex
  - Signed Decimal
  - Unsigned Decimal
  - Binary
  - ASCII
- The existing right-hand 16-byte ASCII preview remains visible in every mode.
- Address/label navigation and Data/Heap/Stack/Custom segment switching remain supported.

### Existing v0.8 fixes retained
- MARS-style data auto-alignment for `.half`, `.word`, `.float`, and `.double`.
- `.align 0` handling.
- Syscall 34 prints `0x` plus eight hexadecimal digits.
- Correct generated C# file line breaks.
- Public `ASMForgeRuntime` API for loading/running/stepping/resetting simulated MIPS and inspecting registers/memory.

## Generated C# example

```csharp
using System;
using ASMForge.Core.Execution;

namespace ASMForgeProject;

internal static class Program
{
    private const string AssemblySource = """
.data
message: .asciiz "Hello from simulated MIPS!\n"

.text
main:
    la $a0, message
    li $v0, 4
    syscall

    li $t0, 5
    li $t1, 6
    add $t2, $t0, $t1

    li $v0, 10
    syscall
""";

    private static void Main()
    {
        var mips = new ASMForgeRuntime();
        mips.LoadAssembly(AssemblySource);
        mips.Run();

        Console.Write(mips.Output);
        Console.WriteLine($"$t2 = {mips.Registers["$t2"]}");
    }
}
```

Pressing **Run** on this file should produce:

```text
Hello from simulated MIPS!
$t2 = 11
```

## Build note

This source tree now has a NuGet dependency on `Microsoft.CodeAnalysis.CSharp` for the built-in C# compiler. Restore packages before building.
