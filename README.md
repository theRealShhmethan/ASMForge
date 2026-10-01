# ASMForge v0.12.0

ASMForge is a MIPS assembly IDE and simulator built with C# and Avalonia. It aims to be a modern,
teacher-ready alternative to MARS: a MARS-compatible assembler, a debugger with breakpoints and step back,
live memory and register viewers, machine-code output, and a C# API for controlling the simulated machine.

See [CHANGELOG.md](CHANGELOG.md) for what changed in each version and
[ASMForge_Roadmap_v0.8.1_Audited.md](ASMForge_Roadmap_v0.8.1_Audited.md) for the plan ahead.

## Features

### Assembler (MARS-compatible)
- Pseudo-instructions are expanded **exactly as MARS does**, using MARS's own `PseudoOps.txt`, so text
  addresses, `jal` return addresses and expansions match MARS (`lw $t0, label` becomes `lui $at` + `lw`, etc.).
- Every instruction is encoded to its 32-bit machine word (R, I and J formats) and shown in the Text Segment
  **Code** column; hover a code cell for its opcode / rs / rt / rd / shamt / funct / immediate fields and binary.
- Data directives with MARS auto-alignment: `.byte .half .word .float .double .ascii .asciiz .space .align`,
  plus `.eqv`, `.globl`, `.extern`, `.set`.
- Clear errors at assemble time: wrong operands (with the accepted forms listed), out-of-range immediates,
  undefined labels, instructions in `.data`, data directives in `.text`, unknown instructions with suggestions.

### Simulator and debugger
- Real MIPS addresses (text `0x00400000`, data `0x10010000`, heap `0x10040000`, stack `0x7FFFEFFC`), little-endian memory.
- **Run** in the background (the window stays responsive, Run I/O updates live), **Pause**, **Stop**, **Continue**.
- **Breakpoints**: click the editor gutter or press Ctrl+B. **Run to Cursor** with Ctrl+F10.
- **Step** and **Step Back**: every step can be undone (registers, HI/LO, memory, PC, heap, console output),
  including stepping back out of a runtime error.
- Registers and memory written by the last step are highlighted, with the previous value on hover.
- Syscalls 1, 4, 5, 8, 9, 10, 11, 12, 17, 30, 34, 35, 36, 40, 41, 42. Read syscalls pause the program and enable an
  input box in Run I/O; Step Back over a read asks for the input again.
- Double-click a register or memory word to edit it (undoable with Step Back); right-click for copy and
  Show Address in Memory / Follow Pointer.

### Editor and workspace
- Tabs, syntax highlighting, MIPS autocomplete, line numbers, System / Light / Dark themes.
- Projects with an Explorer; open files, project, active tab and display formats are restored on the next launch.
- Unsaved files are marked with `*` in the tab and title bar, and ASMForge asks before discarding them.
- Resizable panels, memory viewer with Data / Heap / Stack ($sp) / $gp / $fp / Custom views (register views follow
  the register) and Hex / Signed / Unsigned / Binary / ASCII formats. The Go box accepts addresses, labels and `$registers`.

### Pseudocode generator
- **Tools > Pseudocode Generator** or `.pseudo` files: write C-like pseudocode (variables, arrays, if/else, loops,
  print/read) and get commented MIPS assembly that runs in ASMForge. F3 generates, F5 generates and runs.
  See [docs/PSEUDOCODE.md](docs/PSEUDOCODE.md).

### C# integration
- Press **Run** on a `.cs` file to compile it with Roslyn and run it inside ASMForge; console output goes to Run I/O.
- By default only the active C# file is compiled. Enable **Settings > Run All Project C# Files Together** to
  compile every `.cs` file in the project (the active file's `Main` is used as the entry point).
- New C# files get a class named after the file (`Testing.cs` -> `class Testing`).

## Keyboard shortcuts

| Key | Action |
|---|---|
| F3 | Assemble |
| F5 | Run / Continue |
| F6 | Pause |
| Shift+F5 | Stop (reset to start) |
| F10 | Step |
| F9 | Step Back |
| Ctrl+F10 | Run to Cursor |
| Ctrl+B | Toggle breakpoint |
| Ctrl+N / Ctrl+O / Ctrl+Shift+O | New / Open file / Open project |
| Ctrl+S / Ctrl+Shift+S / Ctrl+W | Save / Save As / Close tab |

## Building and running

Requires the .NET 8 SDK or later.

```bash
dotnet build ASMForge.sln
dotnet test tests/ASMForge.Core.Tests
dotnet run --project src/ASMForge.App
```

The version number lives in `Directory.Build.props`.

## C# interop example

```csharp
using System;
using ASMForge.Core.Execution;

var mips = new ASMForgeRuntime();
var program = mips.LoadAssembly("""
.data
value: .word 41
.text
main:
    lw   $t0, value
    addi $t0, $t0, 1
    li   $v0, 10
    syscall
""");

mips.Run();
Console.WriteLine($"$t0 = {mips.Registers["$t0"]}");                          // 42
Console.WriteLine($"value = {mips.Memory.ReadWord(program.Symbols["value"])}"); // 41

// Debugging from C#: breakpoints, step back, and per-step change records.
mips.Reset();
mips.RunUntil(breakpoints: new HashSet<uint> { program.Symbols["main"] + 8 });
mips.StepBack();
```

## Project layout

| Path | Contents |
|---|---|
| `src/ASMForge.Core` | Assembler, instruction set and encoder, simulator, memory, `ASMForgeRuntime` API |
| `src/ASMForge.App` | Avalonia desktop app |
| `tests/ASMForge.Core.Tests` | xUnit tests for the core |
| `examples/` | Sample programs for the memory viewer, step back and debugger |
| `reference/Mars.jar` | MARS, kept as the compatibility reference |

## Status

See [MARS_PORT_STATUS.md](MARS_PORT_STATUS.md). Not yet supported: floating point (Coprocessor 1) and its syscalls,
file syscalls, Coprocessor 0 exceptions, macros and `.include`, memory-mapped I/O.

## Credits

Pseudo-instruction definitions (`src/ASMForge.Core/Assembly/PseudoOps.txt`) come from MARS 4.5 by
Pete Sanderson and Kenneth Vollmar, MIT license (notice kept in the file). Built with Avalonia, AvaloniaEdit and Roslyn.
