# Changelog

## v0.10.1
- **MARS-accurate pseudo-instructions**: expansions come from MARS's own `PseudoOps.txt` (first-fit,
  basic instructions first, MARS immediate size classes). Text addresses and `jal` return addresses now match MARS.
- Many more pseudo-instructions: `abs`, `rol`/`ror`, `mulu`, `mulo`, immediate forms of `seq`/`sge`/…,
  32-bit immediates on arithmetic, logic and branches, `ulw`/`usw`/`ulh`, `ld`/`sd`, all `la`/load/store addressing forms.
- Fixed: 3-operand `div`/`divu`/`rem`/`remu` computed the wrong result; dividing by zero now stops with
  "Division by zero".
- Assemble-time errors for wrong operands (listing accepted forms), out-of-range immediates, undefined labels,
  `li` with a label (suggests `la`), instructions in `.data`, data directives in `.text`, `.include`/macros.
- **Machine code**: every instruction is encoded (R/I/J and SPECIAL2) and shown in the Text Segment Code column,
  with a field-by-field breakdown on hover. `Instruction.MachineCode` exposes it to C#.
- **Unsaved changes are protected**: closing the window, closing a tab, or creating a new project asks
  Save / Don't Save / Cancel. Unsaved files show `*` in the tab and in the title bar, which also names the active file.
  An empty, never-saved "Untitled" tab no longer counts as unsaved.
- **Help > About ASMForge**: version, runtime, credits, settings and log locations, and an Open Log Folder button.
- Single version number in `Directory.Build.props`; the title bar and About dialog read it at runtime.
- README rewritten for the current feature set; this changelog added.

## v0.10
- **Debugger 1.0**: breakpoints in the editor gutter (click or Ctrl+B), Run stops at breakpoints,
  Continue, Pause (F6), Stop (Shift+F5), Run to Cursor (Ctrl+F10), Clear All Breakpoints.
- MIPS programs run on a background thread; the window stays responsive and Run I/O updates live.
- `MipsMachine.RunUntil` / `ASMForgeRuntime.RunUntil` with a `StopReason` result.
- C#: new **Settings > Run All Project C# Files Together** (off by default compiles only the active file);
  when on, the active file's `Main` is the entry point, avoiding CS0017.
- New C# files get a class named after the file.

## v0.9
- **Step Back** (F9): each step records its register, HI/LO, memory, PC, heap, console and exit-code changes
  and can be undone, including out of a runtime error. Bounded history, cleared on assemble/reset.
- Registers and memory words written by the last step are highlighted, with the previous value on hover.
- Register, memory and text segment views keep their scroll position while stepping.
- Draggable dividers between the Text/Data segments, the output panel, and the register panel.
- Session restore: open files, project folder and active tab reopen on the next launch.
- Register and memory display formats are remembered between sessions.
- Fixed: turning line numbers back on could crash the app; closing the last tab via the File menu could too.
- Fixed: the New dialog was not reachable by Avalonia's XAML runtime loader (missing parameterless constructor).

## v0.8.1
- C# projects run inside ASMForge via in-process Roslyn compilation; output goes to Run I/O and compiler
  errors to Messages with file/line/column.
- New projects create both `main.asm` and `Program.cs`; the New dialog defaults to the open project folder.
- Memory viewer: resizable columns and Hex / Signed / Unsigned / Binary / ASCII display modes.
- Retained from v0.8: MARS-style data auto-alignment, `.align 0`, syscall 34 hex formatting, and the public
  `ASMForgeRuntime` API.
