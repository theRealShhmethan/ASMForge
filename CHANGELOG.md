# Changelog

## Unreleased
- **Pseudocode generator** (Tools > Pseudocode Generator, and `.pseudo` files): write C-like pseudocode and get
  commented MIPS assembly. Supports int variables and arrays, if/else, while, do-while, for, break/continue,
  print/printChar/readInt/readChar/exit and C operators with short-circuit `&&`/`||`. Each statement appears as a
  comment above its instructions; variables are `.data` labels. Errors show line and column with live squiggles.
  Code pasted from an assembly comment block (`# ...` on every line) is accepted as-is. F3 on a `.pseudo` file
  generates `name.asm` beside it; F5 generates and runs. See docs/PSEUDOCODE.md and examples/FizzBuzz.pseudo.
- Generated code follows a hand-written course style: data first, `.globl main`, the pseudocode as one `#` comment
  block, variables in `$s0`-`$s7`, strings named after their text (`fizzBuzz`, `newline`), readable labels
  (`loop`/`endLoop`, `endIf`), and short aligned trailing comments (`# if i > 100 → endLoop`, `# Print i`).

## v0.12.0
- **Live error checking**: about half a second after you stop typing in an assembly file, every line with an
  error gets a red squiggle (unknown instructions, wrong operands, out-of-range values, undefined labels,
  code in the wrong section); numeric registers like `$8` get a yellow warning. Hover the line or put the caret
  on it to see the message. Assemble/runtime errors and C# compiler errors are squiggled too.
- `SimpleAssembler.CheckAll` reports every error in a file (not just the first) without cascading errors.
- A directive written without its dot (`text`, `data`, `word`, …) is reported as "unknown instruction 'text'.
  Did you mean '.text'?" instead of a misleading wrong-section error, and does not cascade onto later lines.
- **Assembly inside C# is checked too**: string literals passed to `LoadAssembly(...)` (directly or through a
  const/variable) get the same live red/yellow squiggles, on the exact line inside `"""` raw and `@"…"` verbatim
  strings. Messages are prefixed with "Assembly:" to tell them apart from C# compiler errors.
- **Double-click a message** in Messages to jump to its file, line and column (assembly, C# compiler errors,
  and C# exception stack traces).
- **Edit menu**: Undo, Redo, Cut, Copy, Paste, Select All, Find (Ctrl+F), Replace (Ctrl+H), Go to Line (Ctrl+G),
  Go to Label (Ctrl+R), Toggle Comment (Ctrl+/), Duplicate Line (Ctrl+D), Move Line Up/Down (Alt+Up/Down).
- App shortcuts now run before the editor, so F3 always assembles (the search panel uses Enter for Find Next).
- **Stability**: unexpected errors no longer close the app. An error dialog explains what happened, keeps
  ASMForge running so you can save, and offers Copy Details and Open Log Folder. A rapid burst of errors still exits.
- Opening an unreadable or vanished file shows an error instead of crashing; project scans skip `bin`, `obj`,
  `.git`, hidden and unreadable folders (faster Explorer on large projects).
- 16 new instruction-semantics regression tests (113 total); Release and Windows x64 builds verified.

## v0.11.0
- **Console input**: syscalls 5 (read integer), 8 (read string, `fgets` semantics like MARS) and 12 (read character).
  The program pauses and Run I/O enables an input box; Enter sends the line and execution resumes the way it was
  going (Run, Step or Run to Cursor). Input is echoed to Run I/O; invalid integers are rejected before sending.
  Step Back over a read discards that input and asks again. `ASMForgeRuntime.ProvideInput("5\n7\n")` for C#.
- Syscalls 30 (system time) and 40-42 (seedable random numbers).
- Run I/O keeps the latest output in view.
- **Editing**: double-click (or right-click > Edit Value) a register or memory word to change it. Accepts hex,
  binary, signed/unsigned decimal and `'c'`. Edits are undoable with Step Back and highlighted in blue.
- **Right-click menus**: registers (Edit, Copy Name, Copy Value, Show Address in Memory); memory words
  (Edit, Copy Value / Address / Row / ASCII, Follow Pointer).
- **Memory views**: Stack ($sp), $gp and $fp views that follow their register while stepping; the Go box accepts
  register names such as `$t0`.

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
