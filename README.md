# ASMForge v0.8

ASMForge v0.8 continues the MARS-compatibility port while keeping the existing C#/Avalonia editor architecture.

## v0.8 highlights

- Working runtime Memory Viewer backed by the same `MipsMemory` used by execution.
- Data, Heap, Stack, and Custom memory views.
- Custom memory jumps accept hexadecimal/decimal addresses or assembled symbol names.
- Memory display refreshes after assemble, step, run, and reset.
- Each memory row shows four 32-bit words plus a 16-byte ASCII preview.
- Fixed MARS-style automatic data alignment so labels on `.half`, `.word`, `.float`, and `.double` point to the aligned address.
- `.align 0` disables automatic numeric alignment until a new `.data`/`.kdata` segment, matching MARS behavior.
- Fixed syscall 34 to print MARS-style `0x` plus eight hexadecimal digits.
- Fixed New C# File generation so the file contains real newlines instead of literal `\n` sequences.
- Added the public `ASMForgeRuntime` C# API in `ASMForge.Core`.
- Added a runnable C# interoperability example under `examples/CSharpInterop`.
- Included `examples/MemoryViewerTest.asm` for testing Data/Heap/Stack updates.

## C# runtime API

A host C# application that references `ASMForge.Core` can now do:

```csharp
var mips = new ASMForgeRuntime();
mips.LoadAssembly(source);
mips.Run();
Console.WriteLine(mips.Registers["$t0"]);
Console.WriteLine(mips.Memory.ReadWord(0x10010000));
```

Available runtime state includes `PC`, `Output`, `ExitCode`, `IsHalted`, `IsRunning`, named/numbered registers, HI/LO, and simulated MIPS memory.

## Memory Viewer test

Open `examples/MemoryViewerTest.asm`, assemble it, then step through it while viewing:

- **Data**: starts at `0x10010000` and contains the declared values/string.
- **Heap**: starts at `0x10040000`; the test writes `0x11223344` there after syscall 9.
- **Stack**: follows `$sp`; the test allocates 16 bytes and writes decimal 99.
- **Custom**: enter an address or a symbol such as `wordValue` and press **Go**.

## Current scope

The v0.8 core covers the integer/source-level subset already ported from MARS. Coprocessor 1, Coprocessor 0, exception/trap handling, full macro/include processing, all remaining syscalls, backstep state restoration, delayed branching, full machine-code encoding/decoding, and other advanced MARS facilities remain future work.

The bundled `reference/Mars.jar` remains the behavioral reference for compatibility work.
