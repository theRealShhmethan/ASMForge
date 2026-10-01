# ASMForge v0.7.2

ASMForge is a learning-focused MIPS/assembly IDE and simulator inspired by MARS, with a modern editor and room for future C# + simulated-assembly projects.

## Fixes in v0.7.2

### Line-number crash fix
- Fixes the AvaloniaEdit shutdown/tab-close crash where the line-number margin could render with an invalid font size (`emSize <= 0`).
- ASMForge now removes line-number margins before an editor is detached and before the main window is torn down.
- Completion popups are closed before editor teardown as well.

### Assembly diagnostics
- Unknown mnemonics now fail during assembly instead of waiting until execution.
- Common typos get a suggestion when a close instruction name exists. Example: `syscal` reports an unknown instruction and suggests `syscall`.
- Invalid register names such as `$t20` fail during assembly.
- Numeric register aliases remain legal, but ASMForge reports a warning explaining the named alias (for example, `$2` is `$v0`).

### Shortcuts and tabs
- **F3** assembles the active assembly file.
- **F5** runs.
- **F10** steps.
- **Ctrl+W** closes the active tab.
- Middle-clicking a file tab closes that tab.

## Existing v0.7 features
- MARS-like EDIT / EXECUTE workspace.
- Text Segment, Data Segment placeholder, Registers, Coproc 0/1 tabs, Messages and Run I/O.
- Project Explorer with project name and ASM/C# source filtering.
- AvaloniaEdit editor with line numbers, syntax highlighting, multi-line Tab/Shift+Tab indentation, themed editor border and MIPS autocomplete.
- Light, Dark and System theme modes.
- Register views: Hex, Signed Decimal, Unsigned Decimal, Binary and ASCII.

## Current simulator status
Full MARS compatibility is still in progress. The machine-code column, full data-segment implementation, complete instruction/directive/syscall coverage, coprocessors and true backstep state restoration remain future work.

## Diagnostics
When running under Visual Studio, ASMForge writes diagnostic messages to **View > Output > Debug**. Persistent logs are also written to:

`%LOCALAPPDATA%\ASMForge\Logs`
