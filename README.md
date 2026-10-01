# ASMForge v0.7.0

ASMForge is a learning-focused MIPS/assembly IDE and simulator inspired by MARS, with a modern editor and room for future C# + simulated-assembly projects.

## What changed in v0.7.0

### More MARS-like workspace
- Registers stay visible on the right in both **EDIT** and **EXECUTE** views.
- **EXECUTE** now shows the Text Segment and Data Segment areas at the same time, closer to MARS.
- **Messages / Run I/O** stay below the main workspace, with **Run I/O** selected by default.
- Added placeholder **Coproc 1** and **Coproc 0** tabs for future MARS compatibility.

### Editor improvements
- Added a clear themed border around the code editor so the editable region is obvious.
- Reworked selection colors for both dark and light themes so highlighted text is less harsh.
- Current execution-line following no longer selects the entire source line. It moves the caret and uses the editor's current-line highlight instead.
- Line numbers, syntax highlighting, and multi-line Tab / Shift+Tab indentation remain available.

### MIPS autocomplete / syntax help
Assembly files now show completion suggestions while typing:
- instructions and pseudo-instructions (`rem`, `li`, `move`, `lw`, branches, etc.)
- directives (`.text`, `.data`, `.asciiz`, `.word`, etc.)
- registers (`$t0`, `$s0`, `$a0`, etc.)
- labels defined in the current source file

Completion entries include a syntax signature and a short explanation. For example, typing `rem` shows:

`rem rd, rs, rt`

with an explanation that the remainder is placed in `rd` and that the pseudo-instruction is implemented through divide/HI behavior.

### Project Explorer
- The project name is now shown explicitly above the source-file list.
- If an `.asmforge` project file is present, ASMForge uses its configured project name.
- Explorer continues to show only `.asm`, `.s`, and `.cs` source files.

## Current simulator status
The simulator is still under active development. The existing instruction subset and basic pseudo-instruction expansion remain functional, but full MARS instruction/directive/syscall compatibility is not complete yet.

The **Code** column in the Text Segment still uses a placeholder until the real MIPS machine-code encoder is implemented.

The **Data Segment**, **Coproc 0**, **Coproc 1**, and true backstep state restoration remain future compatibility work.

## Diagnostics
Diagnostic logging remains enabled during development.

Visual Studio:
- Run ASMForge using **F5**.
- Open **View > Output** and select **Debug**.
- ASMForge messages begin with `[ASMForge]`.

Persistent logs are written under:

`%LOCALAPPDATA%\ASMForge\Logs`
