# ASMForge — Full Development Roadmap

> **Status audit:** Originally audited for **ASMForge v0.8.1**; updated through **v0.12.0** on 2026-10-01 (see CHANGELOG.md).  
> `[x]` means the feature is directly present in the source; `[~]` means it exists in partial form but does not yet satisfy the full roadmap item.  
> `[T]` marks items implemented but not yet confirmed; they become `[x]` after a confirmation pass.


> A long-term development plan for turning ASMForge into a polished, teacher-ready MIPS assembly IDE, simulator, debugger, and C# interoperability environment.

---

# Vision

ASMForge should become a complete educational development environment for learning, writing, assembling, running, and debugging MIPS assembly code.

The finished application should feel like a modern alternative to MARS while keeping the educational features that make MARS useful in class.

The long-term goal is for ASMForge to support:

- MIPS assembly editing
- MARS-compatible assembly behavior
- real MIPS memory simulation
- register inspection
- memory inspection
- stepping and breakpoints
- backstepping
- machine-code visualization
- C# interoperability
- projects with multiple source files
- useful diagnostics
- educational visualization tools
- polished Avalonia UI
- documentation and examples
- automated testing
- exportable project files
- a teacher-ready demo experience

---

# Current Baseline

The current ASMForge builds already include or partially include:

- Avalonia desktop UI
- source editor
- tabbed files
- project creation
- project explorer
- MIPS assembler
- MIPS simulator
- register viewer
- memory viewer
- text segment viewer
- stepping
- backstepping
- Run / Reset
- data labels
- `.data` and `.text`
- common data directives
- many integer MIPS instructions
- pseudo-instructions
- syscall support
- stack / heap / data memory
- MARS-style addressing
- C# project support
- C# compilation through Roslyn
- `ASMForgeRuntime`
- C# → simulated MIPS execution
- C# access to simulated registers and memory
- MARS-style syscall 34 formatting
- MARS-style data auto-alignment

This roadmap begins from that baseline.


### v0.8.1 source-audit additions confirmed

- [x] Built-in Roslyn C# compilation and execution from ASMForge
- [x] Project-wide compilation of `.cs` files
- [x] `Console.Out` and `Console.Error` capture into Run I/O
- [x] C# compiler diagnostics with file, line, column, severity, and diagnostic code
- [x] New projects create both `main.asm` and `Program.cs`
- [x] Middle-click closes an editor tab
- [x] MIPS instruction/register/directive/label autocomplete
- [x] System / Light / Dark theme selector
- [x] Memory viewer Hex / Signed / Unsigned / Binary / ASCII modes
- [x] Register viewer Hex / Signed / Unsigned / Binary / ASCII modes
- [x] Memory navigation by hex address, decimal address, or label
- [x] Data / Heap / Stack / Custom memory views
- [x] MARS-style `.half`, `.word`, `.float`, `.double` auto-alignment and `.align 0`
- [x] Syscalls 1, 4, 9, 10, 11, 17, 34, 35, and 36
- [!] Back button exists, but reverse-state restoration is explicitly not implemented yet *(addressed in v0.9 work below)*
- [!] Text Segment Code column exists, but machine-code encoding is still not implemented (`—`) *(addressed in v0.9: Code column now encoded)*

### v0.9 in progress — per-step change record

> Written without compiling (build not yet run). Items below are `[T]` until `dotnet build` / `dotnet test` pass.

- [x] `MipsMachine` records a `StepRecord` per step: register/HI/LO writes, memory byte writes, PC, heap break, console length, exit code, fault flag
- [x] `MipsMachine.StepBack()` / `ASMForgeRuntime.StepBack()` with bounded history (`HistoryLimit`, default 5000)
- [x] Faulting steps are recorded, so Step Back recovers from runtime errors
- [x] Register and memory viewers highlight values written by the last step, with previous value on hover
- [x] Back button / menu item restore state and are disabled when no history exists
- [x] `tests/ASMForge.Core.Tests/BackstepTests.cs`

---

# Milestone Legend

Use these markers while working through the roadmap:

- [ ] Not started
- [~] In progress
- [x] Complete
- [!] Blocked / bug found
- [T] Needs testing
- [D] Needs documentation
- [S] Stretch goal

---

# Phase 1 — Stabilize the Current Core

## Goal

Make the features that already exist dependable before expanding the application further.

## 1.1 Build Stability

- [x] Ensure the full solution builds cleanly in Visual Studio
- [x] Ensure the full solution builds using `dotnet build` *(0 errors, 0 warnings)*
- [x] Remove all compiler warnings that indicate real issues
- [x] Verify Debug configuration
- [x] Verify Release configuration
- [x] Verify Windows x64 build *(`-r win-x64`, framework-dependent)*
- [ ] Verify portable build if supported *(see Phase 40)*
- [x] Verify application launches with no missing dependencies
- [x] Add a version string visible in the application
- [x] Add About dialog showing version/build information

## 1.2 Crash Prevention

- [T] Audit all UI actions for uncaught exceptions *(plus a top-level handler as a safety net)*
- [x] Prevent crashes when closing tabs
- [T] Prevent crashes when opening malformed files
- [x] Prevent crashes when the active editor has no document
- [x] Prevent crashes when simulator is reset mid-run *(Reset/Step/Assemble blocked while running)*
- [x] Prevent crashes when memory viewer is on an invalid address
- [x] Prevent crashes when register values are edited
- [T] Prevent crashes when project paths no longer exist *(unreadable/deleted folders are skipped)*
- [T] Prevent crashes when files are renamed externally
- [T] Add a top-level exception handler
- [T] Add user-friendly error dialogs

## 1.3 Automated Regression Tests

Create automated tests for:

- [x] arithmetic instructions
- [x] signed arithmetic
- [x] unsigned arithmetic
- [x] multiplication
- [x] division
- [x] HI / LO
- [x] shifts
- [x] comparisons
- [x] jumps
- [x] branches
- [x] register zero behavior
- [x] sign extension
- [x] zero extension
- [x] word loads
- [x] halfword loads
- [x] byte loads
- [x] stores
- [x] alignment errors
- [x] labels
- [x] data directives
- [x] auto alignment
- [x] strings
- [x] heap allocation
- [x] stack behavior
- [x] syscall output
- [x] pseudo-instruction expansion
- [x] reset behavior
- [x] step behavior
- [x] backstep behavior
- [x] C# runtime bridge

## 1.4 Test Suite Organization

- [x] Tests split by area. Actual layout: `InstructionTests` (arithmetic, shifts, comparisons, loads/stores, stack, heap),
      `PseudoInstructionTests`, `MachineCodeTests`, `SyscallTests` (input, random, time, edits), `BackstepTests`,
      `DebuggerTests`, `DiagnosticsTests`, `MachineTests` (directives, labels, C# runtime bridge).

Originally suggested layout:

```text
tests/
├── ArithmeticTests.cs
├── BranchTests.cs
├── MemoryTests.cs
├── DirectiveTests.cs
├── SyscallTests.cs
├── PseudoInstructionTests.cs
├── RuntimeTests.cs
├── CSharpInteropTests.cs
└── RegressionTests.cs
```

---

# Phase 2 — Memory Viewer 2.0

## Goal

Turn the memory viewer into a genuinely useful debugger tool.

## 2.1 Resizable Columns

- [x] Make Address column resizable
- [x] Make +0 column resizable
- [x] Make +4 column resizable
- [x] Make +8 column resizable
- [x] Make +C column resizable
- [x] Make ASCII column resizable
- [x] Set sensible minimum widths
- [x] Prevent values from being visually clipped by default
- [x] Preserve user widths while app remains open
- [S] Persist widths between app launches

## 2.2 Number Display Modes

Add a global memory display selector:

- [x] Hexadecimal
- [x] Signed decimal
- [x] Unsigned decimal
- [x] Binary
- [x] ASCII

Examples:

```text
Hex:
0xDEADBEEF

Signed Decimal:
-559038737

Unsigned Decimal:
3735928559

Binary:
11011110101011011011111011101111

ASCII:
....
```

## 2.3 Address Navigation

- [x] Go to hexadecimal address
- [x] Go to decimal address
- [x] Go to label
- [x] Go to `$sp`
- [x] Go to `$gp`
- [x] Go to `$fp`
- [x] Go to selected register address
- [x] Data shortcut
- [x] Heap shortcut
- [x] Stack shortcut
- [ ] Text shortcut if text memory is exposed

## 2.4 Live Debugging Feedback

- [x] Highlight memory modified by the last instruction
- [x] Highlight byte writes
- [x] Highlight halfword writes
- [x] Highlight word writes
- [x] Clear highlight on next step
- [x] Preserve current viewport during stepping
- [x] Auto-follow `$sp` option *(also $gp and $fp views)*
- [ ] Auto-follow heap option

## 2.5 Memory Editing

- [x] Double-click word to edit
- [x] Hex input
- [x] Decimal input
- [x] Binary input
- [x] Validate values
- [ ] Prevent edits outside valid memory if appropriate
- [ ] Show confirmation for dangerous edits
- [x] Make memory edits participate in backstep history

## 2.6 Memory Context Menu

- [x] Copy address
- [x] Copy value
- [x] Copy row
- [x] Copy ASCII
- [x] Go to address *(Follow Pointer, and Show Address in Memory on registers)*
- [ ] Add watch
- [x] Edit value
- [ ] Fill memory
- [S] Save region to file

---

# Phase 3 — Register Viewer 2.0

## Goal

Make registers as useful and readable as the improved memory viewer.

## 3.1 Display Modes

- [x] Hex
- [x] Signed decimal
- [x] Unsigned decimal
- [x] Binary
- [x] ASCII interpretation

## 3.2 Register Groups

Display groups for:

- [x] `$zero`
- [x] `$at`
- [x] `$v0-$v1`
- [x] `$a0-$a3`
- [x] `$t0-$t9`
- [x] `$s0-$s7`
- [x] `$k0-$k1`
- [x] `$gp`
- [x] `$sp`
- [x] `$fp`
- [x] `$ra`
- [x] HI
- [x] LO
- [x] PC

## 3.3 Debug Feedback

- [x] Highlight registers changed by last step
- [x] Different highlight for manually edited register
- [x] Show previous value on hover
- [x] Copy register name
- [x] Copy register value
- [x] Right-click → Go to address in memory
- [ ] Register search/filter

## 3.4 Register Editing

- [x] Edit register values
- [x] Block modification of `$zero`
- [x] Support hex entry
- [x] Support signed decimal entry
- [x] Support unsigned decimal entry
- [x] Support binary entry
- [x] Include register edits in backstep state

---

# Phase 4 — Text Segment / Machine Code

## Goal

Make the Text Segment useful for understanding how assembly becomes machine instructions.

## 4.1 Populate Machine Code Column

Show values such as:

```text
Address       Code         Basic
0x00400000    0x3C081001   lui $t0, 4097
0x00400004    0x35080000   ori $t0, $t0, 0
```

Tasks:

- [x] Encode R-type instructions
- [x] Encode I-type instructions
- [x] Encode J-type instructions
- [x] Expand pseudo-instructions into MARS-identical basic instructions (prerequisite for encoding)
- [x] Encode pseudo-instruction expansions
- [x] Display generated binary *(Code cell tooltip)*
- [x] Display generated hexadecimal
- [x] Verify encoding against MARS *(unit tests use standard MIPS32 encodings; spot-check in MARS)*

## 4.2 Source Mapping

- [x] Preserve original source line
- [x] Show expanded basic instruction
- [x] Show machine code
- [x] Show address
- [ ] Allow clicking row to jump to editor
- [ ] Allow clicking source to highlight corresponding expanded rows
- [x] Show multiple machine instructions generated by pseudo-instruction

## 4.3 Execution Highlighting

- [~] Highlight current PC row
- [ ] Highlight previous instruction
- [x] Scroll current instruction into view
- [x] Make current instruction visually distinct
- [~] Follow execution toggle

## 4.4 Instruction Details

On hover or selection show:

- [x] opcode
- [x] funct field
- [x] rs
- [x] rt
- [x] rd
- [x] shamt
- [x] immediate
- [x] target
- [x] 32-bit binary representation

---

# Phase 5 — Debugger 1.0

## Goal

Make ASMForge feel like a real debugger.

## 5.1 Breakpoints

- [x] Click gutter to add breakpoint
- [x] Click again to remove
- [x] Show breakpoint icon
- [x] Persist breakpoints while project is open *(while the file stays open; not saved across restarts yet)*
- [x] Run until breakpoint
- [x] Skip invalid breakpoint lines
- [ ] Breakpoint list window
- [ ] Enable / disable breakpoint
- [x] Remove all breakpoints

## 5.2 Conditional Breakpoints

- [ ] Register condition
- [ ] memory condition
- [ ] expression conditions

Examples:

```text
$t0 == 10
$sp < 0x7FFFF000
mem[0x10010000] == 5
```

## 5.3 Execution Controls

- [x] Run
- [x] Pause
- [x] Stop
- [x] Reset
- [x] Step Into
- [x] Step Back
- [x] Run to Cursor
- [ ] Restart
- [x] Continue

## 5.4 Execution Speed

- [ ] Full speed
- [ ] Slow mode
- [ ] adjustable instructions-per-second
- [ ] animated step mode

Possible options:

```text
1 instruction/sec
5 instructions/sec
10 instructions/sec
100 instructions/sec
Maximum
```

## 5.5 Call Stack

- [ ] Track function calls from `jal`
- [ ] Track returns from `jr $ra`
- [ ] Show call stack window
- [ ] Show return addresses
- [ ] Jump to function source
- [S] Stack frame visualization

---

# Phase 6 — Backstepping 2.0

## Goal

Make reverse execution reliable and useful for education.

- [x] restore register changes
- [x] restore memory writes
- [x] restore PC
- [x] restore HI / LO
- [x] restore heap pointer
- [x] restore console state where practical
- [x] restore stack writes
- [x] restore manual memory edits
- [x] restore manual register edits
- [x] keep bounded history
- [~] configurable history size *(API only: `HistoryLimit`; no settings UI yet)*
- [x] clear history on reset
- [x] visual indicator when backstep is available

Stretch goal:

- [S] Timeline showing executed instructions

---

# Phase 7 — Complete MIPS Integer Instruction Coverage

## Goal

Support a broad MARS-compatible integer instruction set.

## Arithmetic

- [x] `add`
- [x] `addu`
- [x] `addi`
- [x] `addiu`
- [x] `sub`
- [x] `subu`
- [x] `mult`
- [x] `multu`
- [x] `div`
- [x] `divu`
- [x] `mfhi`
- [x] `mflo`
- [x] `mthi`
- [x] `mtlo`

## Logical

- [x] `and`
- [x] `andi`
- [x] `or`
- [x] `ori`
- [x] `xor`
- [x] `xori`
- [x] `nor`

## Shifts

- [x] `sll`
- [x] `sllv`
- [x] `srl`
- [x] `srlv`
- [x] `sra`
- [x] `srav`

## Comparison

- [x] `slt`
- [x] `sltu`
- [x] `slti`
- [x] `sltiu`

## Branches

- [x] `beq`
- [x] `bne`
- [x] `bgez`
- [x] `bgtz`
- [x] `blez`
- [x] `bltz`
- [x] branch-and-link forms where supported

## Jumps

- [x] `j`
- [x] `jal`
- [x] `jr`
- [x] `jalr`

## Loads

- [x] `lb`
- [x] `lbu`
- [x] `lh`
- [x] `lhu`
- [x] `lw`
- [x] `lwl`
- [x] `lwr`

## Stores

- [x] `sb`
- [x] `sh`
- [x] `sw`
- [x] `swl`
- [x] `swr`

---

# Phase 8 — Pseudo-Instructions

## Goal

Match common MARS pseudo-instruction behavior.

Support and verify:

- [x] `li`
- [x] `la`
- [x] `move`
- [ ] `clear`
- [x] `not`
- [x] `neg`
- [x] `negu`
- [x] `abs`
- [x] `mul`
- [x] `rem`
- [x] `remu`
- [x] `b`
- [x] `bal`
- [x] `beqz`
- [x] `bnez`
- [x] `blt`
- [x] `ble`
- [x] `bgt`
- [x] `bge`
- [x] unsigned comparison branches
- [x] rotate pseudo-instructions
- [x] load-address variants

For every pseudo-instruction:

- [x] show source instruction
- [x] show expansion
- [x] show generated machine code
- [x] match MARS result *(expansions come from MARS's own PseudoOps.txt)*
- [x] test edge-case immediates

---

# Phase 9 — Assembler Directives

## Goal

Expand source compatibility with MARS.

## Data Directives

- [x] `.byte`
- [x] `.half`
- [x] `.word`
- [x] `.float`
- [x] `.double`
- [x] `.ascii`
- [x] `.asciiz`
- [x] `.space`
- [x] `.align`

## Source / Symbol Directives

- [x] `.text`
- [x] `.data`
- [x] `.globl`
- [x] `.eqv`
- [ ] `.include`
- [x] `.extern`
- [x] `.set`

## Alignment

- [x] automatic `.half` alignment
- [x] automatic `.word` alignment
- [x] automatic `.float` alignment
- [x] automatic `.double` alignment
- [x] `.align 0`
- [~] compare alignment behavior against MARS

---

# Phase 10 — Macros

## Goal

Support reusable assembly macros.

Example:

```asm
.macro print_int(%reg)
    move $a0, %reg
    li $v0, 1
    syscall
.end_macro
```

Tasks:

- [ ] parse `.macro`
- [ ] parse `.end_macro`
- [ ] macro arguments
- [ ] nested macro expansion
- [ ] macro diagnostics
- [ ] source mapping
- [ ] recursion protection
- [ ] test against MARS behavior

---

# Phase 11 — Syscall Compatibility

## Goal

Implement a broad set of MARS syscalls.

## Console

- [x] 1 — Print integer
- [ ] 2 — Print float
- [ ] 3 — Print double
- [x] 4 — Print string
- [x] 5 — Read integer
- [ ] 6 — Read float
- [ ] 7 — Read double
- [x] 8 — Read string
- [x] 11 — Print character
- [x] 12 — Read character

## Runtime

- [x] 9 — `sbrk`
- [x] 10 — Exit
- [x] 17 — Exit with code

## Files

- [ ] 13 — Open file
- [ ] 14 — Read file
- [ ] 15 — Write file
- [ ] 16 — Close file

## Extended MARS Syscalls

- [x] 30 — System time
- [ ] 31 — MIDI output
- [ ] 32 — Sleep
- [ ] 33 — MIDI output synchronous
- [x] 34 — Print integer hexadecimal
- [x] 35 — Print integer binary
- [x] 36 — Print integer unsigned
- [x] 40+ random number syscalls where appropriate

## I/O UI

- [x] input prompt UI
- [ ] console input history
- [ ] EOF handling
- [ ] clear console
- [ ] copy console output
- [ ] save console output

---

# Phase 12 — Floating-Point / Coprocessor 1

## Goal

Support common MIPS floating-point programs.

## Register Viewer

Add:

- [ ] `$f0-$f31`
- [ ] float mode
- [ ] double mode
- [ ] raw hex mode

## Instructions

- [ ] `lwc1`
- [ ] `swc1`
- [ ] `ldc1`
- [ ] `sdc1`
- [ ] `mov.s`
- [ ] `mov.d`
- [ ] `add.s`
- [ ] `sub.s`
- [ ] `mul.s`
- [ ] `div.s`
- [ ] `add.d`
- [ ] `sub.d`
- [ ] `mul.d`
- [ ] `div.d`
- [ ] `c.eq.s`
- [ ] `c.lt.s`
- [ ] `c.le.s`
- [ ] double comparison equivalents
- [ ] `bc1t`
- [ ] `bc1f`
- [ ] conversion instructions

## Validation

- [ ] NaN behavior
- [ ] infinity behavior
- [ ] negative zero
- [ ] precision
- [ ] MARS comparison tests

---

# Phase 13 — Coprocessor 0 and Exceptions

## Goal

Support more realistic CPU behavior.

- [ ] Coprocessor 0 register model
- [ ] Status
- [ ] Cause
- [ ] EPC
- [ ] BadVAddr
- [ ] arithmetic overflow exceptions
- [ ] address errors
- [ ] invalid instruction exceptions
- [ ] syscall exceptions if modeled
- [ ] `mfc0`
- [ ] `mtc0`
- [ ] exception handler addresses
- [ ] exception viewer panel
- [ ] helpful exception messages

---

# Phase 14 — Memory-Mapped I/O

## Goal

Support educational hardware-style interaction.

- [ ] keyboard receiver
- [ ] keyboard control register
- [ ] display transmitter
- [ ] display control register
- [ ] MMIO enable toggle
- [ ] MMIO console panel
- [ ] keyboard input testing
- [ ] text output testing
- [S] simple device plugin system

---

# Phase 15 — Editor 2.0

## Goal

Make assembly editing feel like a modern IDE.

## Syntax Highlighting

- [x] instructions
- [x] pseudo-instructions
- [x] registers
- [x] labels
- [x] numbers
- [x] strings
- [x] directives
- [x] comments
- [ ] macros

## Editing Features

- [x] auto-indent
- [ ] tab / spaces settings
- [x] line numbers
- [x] current-line highlight
- [ ] bracket matching
- [T] comment/uncomment shortcut
- [T] duplicate line
- [T] move line up/down
- [T] find
- [T] replace
- [ ] find in files
- [T] go to line
- [T] go to label
- [ ] code folding

## IntelliSense / Suggestions

- [x] instruction autocomplete
- [x] register autocomplete
- [x] directive autocomplete
- [x] label autocomplete
- [ ] syscall snippets
- [~] hover instruction documentation
- [~] parameter hints

## Error Visualization

- [T] red squiggles *(live while typing; every bad line, not just the first)*
- [T] warning squiggles
- [T] hover diagnostic
- [T] click error → line
- [T] Messages panel integration

---

# Phase 16 — Project Explorer 2.0

## Goal

Make projects reliable and easy to manage.

## File Operations

- [x] New File
- [ ] New Folder
- [ ] Rename
- [ ] Delete
- [ ] Duplicate
- [ ] Move
- [ ] Open externally
- [ ] Open in File Explorer
- [ ] Copy full path
- [ ] Copy relative path

## Project Behavior

- [x] automatically include new files
- [ ] detect externally added files
- [ ] detect externally removed files
- [ ] refresh project
- [x] persist project metadata
- [x] reopen previous tabs
- [x] remember active file
- [ ] remember expanded folders

## Icons

- [ ] `.asm`
- [ ] `.s`
- [ ] `.cs`
- [ ] `.json`
- [ ] `.md`
- [ ] folders
- [ ] project root

---

# Phase 17 — Multi-File Assembly Projects

## Goal

Allow larger MIPS projects instead of one-file demos.

Example:

```text
MyProject/
├── main.asm
├── math.asm
├── strings.asm
└── helpers.asm
```

Tasks:

- [ ] assemble multiple files together
- [ ] shared global symbols
- [ ] duplicate-label detection
- [x] `.globl`
- [ ] external symbol resolution
- [ ] include ordering
- [ ] project entry point
- [ ] per-file diagnostics
- [ ] source mapping across files

---

# Phase 18 — C# Integration 2.0

## Goal

Make C# integration a first-class ASMForge feature.

## Compiler

- [x] Roslyn compilation
- [x] multiple C# source files
- [x] project-wide compilation
- [x] compiler diagnostics
- [~] warning levels
- [x] choose entry point: active file's Main when compiling all project files
- [x] setting: compile only the active file (default) or all project C# files
- [T] click diagnostic → file/line

## Runtime

- [x] execute `Main`
- [x] capture stdout
- [x] capture stderr
- [ ] Stop / cancellation
- [ ] timeout protection
- [ ] safe execution boundary where practical

## ASMForgeRuntime API

Maintain APIs such as:

```csharp
var mips = new ASMForgeRuntime();

mips.LoadAssembly(source);
mips.Run();
mips.Step();
mips.Reset();

int value = mips.Registers["$t0"];
mips.Registers["$t0"] = 10;

uint word = mips.Memory.ReadWord(0x10010000);
mips.Memory.WriteWord(0x10010000, 123);

Console.WriteLine(mips.Output);
```

Add:

- [ ] events for register change
- [ ] events for memory change
- [ ] events for instruction executed
- [ ] events for syscall
- [x] breakpoint API (`RunUntil`)
- [ ] load from file
- [ ] load from project
- [ ] symbol lookup
- [ ] memory region enumeration
- [ ] execution statistics

## C# + Assembly Projects

Support:

```text
InteropProject/
├── Program.cs
├── main.asm
└── helper.asm
```

- [ ] C# can load project assembly automatically
- [x] C# can inspect symbols
- [x] C# can manipulate simulated memory
- [ ] C# can invoke MIPS functions
- [S] MIPS can invoke registered C# host functions

---

# Phase 19 — Educational Tools

## Goal

Make ASMForge useful specifically for students and teachers.

## Instruction Explanation Panel

When an instruction is selected:

```asm
add $t2, $t0, $t1
```

Show:

```text
Operation:
$t2 = $t0 + $t1

Type:
R-Type

Registers:
rd = $t2
rs = $t0
rt = $t1

Machine Code:
0x01095020
```

Tasks:

- [ ] instruction explanation
- [ ] pseudo-instruction explanation
- [ ] register effects
- [ ] memory effects
- [ ] machine-code fields
- [ ] common mistakes

## Visual Datapath

Stretch goal:

- [S] show simplified CPU datapath
- [S] highlight active components
- [S] show ALU operation
- [S] show memory access
- [S] show register read/write
- [S] show branch decision

## Step Explanation

After each step:

```text
Executed:
lw $t0, 0($t1)

Address calculated:
0x10010000

Memory value:
0x0000002A

Result:
$t0 changed from 0 to 42
```

- [ ] optional explanation mode
- [ ] beginner / advanced toggle

---

# Phase 20 — Stack Visualizer

## Goal

Make function calls easier to understand.

Show:

```text
High Memory

0x7FFFEFFC  saved $ra
0x7FFFEFF8  saved $s0
0x7FFFEFF4  local variable

<- $sp

Low Memory
```

Tasks:

- [ ] follow `$sp`
- [ ] annotate saved `$ra`
- [ ] annotate saved registers
- [ ] show words
- [ ] show stack growth direction
- [ ] detect probable stack frames
- [S] visualize nested calls

---

# Phase 21 — Heap Visualizer

## Goal

Make `sbrk` and dynamic memory visible.

- [ ] show heap start
- [ ] show current heap pointer
- [ ] show allocations
- [ ] allocation sizes
- [ ] visualize used / unused regions
- [ ] jump to allocation
- [S] optional labels for allocations from C# API

---

# Phase 22 — Console / Run I/O 2.0

## Goal

Create a polished terminal experience.

- [ ] ANSI-safe text rendering
- [x] input support
- [ ] output history
- [ ] clear button
- [ ] copy all
- [ ] save output
- [x] input prompt
- [ ] distinguish program output from debugger output
- [ ] distinguish errors
- [ ] timestamps optional
- [ ] monospace font controls

Tabs could include:

```text
Run I/O
Build
Messages
Debug
```

---

# Phase 23 — Diagnostics and Messages

## Goal

Provide clear, student-friendly errors.

## Assembly Errors

Examples:

```text
main.asm(14,5): Unknown instruction 'ad'
main.asm(18,12): Register '$t12' does not exist
main.asm(24,1): Label 'loop' already defined
```

## Runtime Errors

Examples:

```text
Runtime Error at 0x00400018:
Unaligned word address 0x10010001

Instruction:
lw $t0, 0($t1)
```

Tasks:

- [~] file
- [~] line
- [~] column
- [~] severity
- [~] message
- [~] code
- [T] jump-to-source
- [ ] copy diagnostic

---

# Phase 24 — Preferences / Settings

## Editor

- [ ] font family
- [ ] font size
- [ ] tabs vs spaces
- [ ] tab width
- [ ] word wrap
- [ ] show whitespace

## Appearance

- [x] light theme
- [x] dark theme
- [x] system theme
- [ ] accent color
- [ ] compact / comfortable density

## Simulator

- [ ] delayed branching mode
- [ ] start address
- [ ] memory configuration
- [ ] backstep history size
- [ ] execution speed

## Memory Viewer

- [ ] default format
- [ ] remember column widths
- [ ] uppercase/lowercase hex
- [ ] show `0x` prefix
- [ ] ASCII mode preferences

---

# Phase 25 — Keyboard Shortcuts

Suggested defaults:

```text
Ctrl+N       New File
Ctrl+O       Open File
Ctrl+S       Save
Ctrl+Shift+S Save As
Ctrl+F       Find
Ctrl+H       Replace
F5           Run
Shift+F5     Stop
F10          Step
F9           Step Back
Ctrl+B       Toggle Breakpoint
Ctrl+G       Go to Line
Ctrl+Shift+G Go to Address
Ctrl+`       Focus Run I/O
```

Tasks:

- [ ] shortcut manager
- [ ] editable shortcuts
- [ ] conflict detection
- [ ] reset defaults

---

# Phase 26 — UI Layout and Docking

## Goal

Make the application comfortable on different screen sizes.

- [x] resizable panels *(drag dividers: Text/Data Segment, workspace/output, registers)*
- [ ] remember pane sizes
- [ ] collapsible Project Explorer
- [ ] collapsible right debugger panel
- [ ] collapsible bottom console
- [ ] reset layout
- [ ] full-screen editor
- [S] detachable panes
- [S] dockable panels

Suggested layout:

```text
+-------------------------------------------------------------+
| Toolbar                                                     |
+-------------+---------------------------+-------------------+
| Explorer    | Editor                    | Registers         |
|             |                           | Memory            |
|             |                           | Stack             |
+-------------+---------------------------+-------------------+
| Run I/O / Messages / Build / Debug                           |
+-------------------------------------------------------------+
```

---

# Phase 27 — File Formats and Export

- [x] Open `.asm`
- [x] Open `.s`
- [x] Open `.cs`
- [x] project format
- [ ] recent projects
- [ ] export project ZIP
- [ ] import project ZIP
- [ ] export memory dump
- [ ] export register dump
- [ ] export machine-code listing
- [ ] export binary
- [ ] export hex listing
- [ ] printable assembly listing

---

# Phase 28 — Comparison With MARS

## Goal

Create a compatibility test matrix.

For each test program:

```text
Program
ASMForge Output
MARS Output
Match?
Notes
```

Test:

- [ ] arithmetic
- [ ] branching
- [ ] pseudo-instructions
- [ ] strings
- [ ] stack
- [ ] heap
- [ ] syscalls
- [ ] macros
- [ ] alignment
- [ ] floating point
- [ ] exceptions
- [ ] machine code
- [ ] memory layout

Create:

```text
docs/MARS_COMPATIBILITY.md
```

Possible status values:

```text
✅ Compatible
🟡 Partial
❌ Missing
⚠ Intentional Difference
```

---

# Phase 29 — Performance

## Goal

Keep large programs responsive.

- [ ] benchmark assembly time
- [ ] benchmark simulator speed
- [ ] benchmark memory viewer
- [ ] virtualize large memory tables
- [x] avoid refreshing entire UI every instruction at full speed
- [x] batch UI updates during Run
- [ ] avoid unnecessary allocations
- [ ] profile large source files
- [ ] profile 1,000,000+ instruction runs

---

# Phase 30 — Accessibility

- [ ] keyboard-accessible UI
- [ ] visible focus indicators
- [ ] screen-reader labels
- [ ] high contrast support
- [ ] scalable fonts
- [ ] avoid color-only debugging indicators
- [ ] configurable highlight styles
- [ ] accessible error messages

---

# Phase 31 — Documentation

Create a full documentation directory:

```text
docs/
├── GETTING_STARTED.md
├── INSTALLATION.md
├── PROJECTS.md
├── ASSEMBLER.md
├── DEBUGGER.md
├── MEMORY_VIEWER.md
├── REGISTERS.md
├── CSHARP_INTEROP.md
├── MARS_COMPATIBILITY.md
├── SHORTCUTS.md
├── FAQ.md
└── TROUBLESHOOTING.md
```

## Getting Started

Explain:

- [ ] creating a project
- [ ] opening an assembly file
- [ ] assembling
- [ ] running
- [ ] stepping
- [ ] viewing registers
- [ ] viewing memory
- [ ] using breakpoints

## C# Documentation

Explain:

- [ ] creating C# project
- [ ] `ASMForgeRuntime`
- [ ] loading assembly
- [ ] reading registers
- [ ] writing registers
- [ ] reading memory
- [ ] writing memory
- [ ] running
- [ ] stepping
- [ ] handling output

---

# Phase 32 — Example Programs

Create a strong example library.

```text
examples/
├── HelloWorld.asm
├── Arithmetic.asm
├── Loops.asm
├── FizzBuzz.asm
├── Arrays.asm
├── Strings.asm
├── Functions.asm
├── Stack.asm
├── Recursion.asm
├── Heap.asm
├── FileIO.asm
├── FloatingPoint.asm
├── MemoryMappedIO.asm
├── CSharpInterop/
└── DebuggerDemo/
```

Recommended examples:

- [ ] Hello World
- [ ] sum two numbers
- [ ] FizzBuzz
- [ ] factorial
- [ ] Fibonacci
- [ ] array sum
- [ ] array maximum
- [ ] string length
- [ ] string reverse
- [ ] function call
- [ ] nested function calls
- [ ] recursion
- [ ] stack frame
- [ ] heap allocation
- [ ] file read/write
- [ ] floating-point math
- [x] C# controls MIPS execution

---

# Phase 33 — Teacher Demo Mode

## Goal

Create a polished demonstration specifically for showing ASMForge to an instructor.

Add a sample project:

```text
TeacherDemo/
├── main.asm
├── functions.asm
├── Program.cs
└── README.md
```

The demo should show:

1. Create/open project
2. Syntax highlighting
3. Assemble
4. Pseudo-instruction expansion
5. Machine-code generation
6. Run program
7. Step program
8. Register changes
9. Memory changes
10. Breakpoint
11. Backstep
12. Stack visualization
13. Heap visualization
14. C# → MIPS interop
15. Error diagnostics

## Demo Program Idea

A small program that:

- creates an integer array
- calls a function
- loops over values
- uses stack storage
- computes a result
- writes result to memory
- prints result
- is then inspected from C#

This demonstrates nearly the entire application in one project.

---

# Phase 34 — Built-In Tutorials

## Goal

Turn ASMForge into a learning tool.

Possible tutorial sequence:

### Tutorial 1
Registers and `li`

### Tutorial 2
Arithmetic

### Tutorial 3
Memory

### Tutorial 4
Branches

### Tutorial 5
Loops

### Tutorial 6
Functions

### Tutorial 7
Stack

### Tutorial 8
Arrays

### Tutorial 9
Syscalls

### Tutorial 10
Machine Code

Each tutorial can contain:

- explanation
- starter code
- task
- expected result
- Run Test button
- hints

---

# Phase 35 — Instruction Reference

Add an internal searchable instruction reference.

For every instruction show:

```text
Mnemonic:
add

Syntax:
add $d, $s, $t

Description:
Adds two signed integers.

Operation:
R[d] = R[s] + R[t]

Overflow:
Raises arithmetic overflow exception.

Type:
R

Opcode:
000000

Funct:
100000
```

Include:

- [ ] search
- [ ] category filter
- [ ] examples
- [ ] pseudo-instruction notes
- [ ] MARS differences if any

---

# Phase 36 — Watch Window

## Goal

Allow users to monitor specific values while debugging.

Examples:

```text
$t0
$t1
0x10010000
myArray
myArray + 12
$sp
```

Tasks:

- [ ] add watch
- [ ] remove watch
- [ ] refresh watches
- [ ] display format
- [ ] highlight changed watches
- [ ] label resolution
- [ ] memory dereference expressions

---

# Phase 37 — Symbol Browser

Show:

```text
Labels
------
main              0x00400000
loop              0x00400018
printResult       0x00400040

Data
----
message           0x10010000
numbers           0x10010020
```

Features:

- [ ] text symbols
- [ ] data symbols
- [ ] address
- [ ] source file
- [ ] source line
- [ ] double-click to navigate

---

# Phase 38 — Search Across Project

- [ ] Find in Files
- [ ] Replace in Files
- [ ] search labels
- [ ] search instructions
- [ ] search comments
- [ ] regex mode
- [ ] case-sensitive mode
- [ ] whole-word mode
- [ ] result panel
- [ ] click result → source

---

# Phase 39 — Session Recovery

- [ ] autosave optional
- [ ] recover unsaved files after crash
- [x] reopen project
- [x] reopen tabs
- [ ] restore cursor positions
- [ ] restore breakpoints
- [ ] restore panel layout
- [x] restore memory/register display preferences

---

# Phase 40 — Installer and Distribution

## Windows

- [ ] self-contained build
- [ ] installer
- [ ] Start Menu shortcut
- [ ] Desktop shortcut optional
- [ ] file associations
- [ ] uninstall support
- [ ] versioned installer
- [ ] application icon

Potential installer:

- [ ] MSIX
- [ ] Inno Setup
- [ ] NSIS

## Portable

- [ ] portable ZIP
- [ ] no installation required
- [ ] settings stored appropriately

---

# Phase 41 — Update System

- [ ] check latest version
- [ ] show release notes
- [ ] download update
- [ ] optional automatic check
- [ ] skip version
- [ ] GitHub Releases integration
- [ ] update error handling

---

# Phase 42 — GitHub / Open Source Readiness

Repository should include:

```text
README.md
LICENSE
CONTRIBUTING.md
CHANGELOG.md
ROADMAP.md
SECURITY.md
docs/
examples/
src/
tests/
```

README should show:

- [ ] screenshot
- [ ] what ASMForge is
- [ ] major features
- [ ] installation
- [ ] quick start
- [ ] screenshots/GIFs
- [ ] project status
- [ ] roadmap
- [ ] credits
- [ ] MARS compatibility note
- [ ] license information

---

# Phase 43 — Credits and Licensing

Because ASMForge uses MARS as a behavioral/source reference:

- [ ] preserve required MARS license notices where applicable
- [ ] document MARS inspiration/reference
- [ ] include third-party licenses
- [ ] include Roslyn license notices if required
- [ ] include Avalonia license notices if required
- [ ] audit NuGet packages
- [ ] add `THIRD_PARTY_NOTICES.md`

---

# Phase 44 — Release Candidate Checklist

Before calling ASMForge 1.0:

## Reliability

- [ ] no known startup crashes
- [ ] no known project corruption bugs
- [ ] no known memory viewer crashes
- [ ] no known register viewer crashes
- [ ] assembler handles invalid input safely
- [ ] simulator handles invalid input safely

## MIPS

- [x] common integer instruction set
- [~] pseudo-instructions
- [x] major directives
- [~] common syscalls
- [x] stack
- [x] heap
- [x] memory
- [x] labels
- [x] machine code

## Debugger

- [x] Run
- [x] Stop
- [x] Step
- [x] Backstep
- [x] breakpoints
- [x] registers
- [x] memory
- [x] changed-value highlighting

## Projects

- [x] create project
- [x] open project
- [x] save project
- [x] multiple files
- [ ] rename files
- [ ] delete files
- [ ] recent projects

## C#

- [x] compile
- [x] run
- [x] multiple files
- [x] console output
- [x] compiler errors
- [x] ASMForgeRuntime

## Documentation

- [ ] getting started
- [ ] debugging guide
- [ ] C# guide
- [ ] examples
- [ ] compatibility status

---

# Phase 45 — ASMForge 1.0

ASMForge 1.0 should be something that can confidently be shown to an instructor and described as:

> A modern MIPS assembly IDE and simulator built with C# and Avalonia, with integrated debugging, memory/register visualization, MARS-compatible assembly features, and a C# interoperability API.

## 1.0 Showcase Features

Aim to have these polished:

- [~] modern code editor
- [x] project explorer
- [x] MIPS assembler
- [x] simulator
- [x] MARS-compatible memory model
- [x] register viewer
- [x] memory viewer
- [x] machine-code viewer
- [x] breakpoints
- [~] step / backstep
- [ ] stack visualization
- [ ] heap visualization
- [x] syscall I/O
- [~] clear diagnostics
- [x] C# integration
- [ ] documentation
- [~] examples
- [ ] installer

---

# Phase 46 — Post-1.0 Ideas

These are not required for a strong teacher demo, but could make ASMForge genuinely unique.

## [S] RISC-V Support

- RISC-V registers
- RISC-V assembler
- simulator
- architecture selector

## [S] ARM Educational Mode

Basic ARM assembly simulation.

## [S] CPU Pipeline Visualization

Show:

```text
IF → ID → EX → MEM → WB
```

Include:

- hazards
- forwarding
- stalls
- branches

## [S] Cache Simulator

- direct mapped
- set associative
- configurable line size
- hit/miss counters
- visualization

## [S] Performance Statistics

Show:

```text
Instructions executed
Loads
Stores
Branches
Taken branches
Syscalls
Estimated CPI
```

## [S] Assembly Unit Tests

Example:

```asm
@test test_add
    li $t0, 5
    li $t1, 6
    add $t2, $t0, $t1
    assert_eq $t2, 11
@endtest
```

## [S] Plugin System

Potential plugins:

- alternate architectures
- devices
- visualization tools
- classroom tools

## [S] Classroom Mode

Teacher could distribute:

- starter project
- locked test cases
- expected outputs

ASMForge could run automated checks without exposing answers.

---

# Phase 47 — Pseudocode-to-MIPS Generator (Tools menu)

## Goal

A deterministic (non-AI) translator: write simple pseudocode in a defined syntax and generate readable,
commented MIPS assembly that assembles and runs in ASMForge. Listed under **Tools > Pseudocode Generator**.

## Design (to decide when this phase starts)

- [ ] Define the pseudocode language (variables, integer arithmetic, assignment, if/else, while, for,
      print / read, arrays, functions with parameters and return values)
- [ ] Write a short language reference with examples
- [ ] Decide register/stack allocation strategy (e.g. variables in `.data`, temporaries in `$t` registers)

## Implementation

- [ ] Tokenizer and parser with clear error messages (line/column)
- [ ] Code generator producing commented MIPS (`# x = y + 1`) that maps back to pseudocode lines
- [ ] Tools > Pseudocode Generator window: pseudocode editor, live preview of generated assembly
- [ ] Open the result as a new `.asm` tab / insert into the current file
- [ ] Generated code assembles cleanly and passes round-trip tests (generate, assemble, run, check output)
- [ ] Example pseudocode programs

---

# Recommended Development Order

The roadmap above is intentionally huge. A practical order from the current build is:

## Immediate

1. Memory Viewer 2.0
2. Register Viewer 2.0
3. machine-code generation
4. breakpoint system
5. changed-value highlighting
6. project explorer improvements
7. stronger diagnostics

## Next

8. complete integer instructions
9. pseudo-instruction coverage
10. syscalls
11. multi-file projects
12. C# integration 2.0
13. stack visualizer
14. heap visualizer

## Teacher Demo Milestone

15. example library
16. teacher demo project
17. documentation
18. installer
19. About screen
20. visual polish

## Advanced

21. Coprocessor 1
22. Coprocessor 0
23. exceptions
24. MMIO
25. macros
26. tutorials
27. instruction reference

---

# Suggested Version Plan

> **Actual releases so far** (see CHANGELOG.md): v0.9 Step Back, change highlighting, session restore; v0.10 Debugger 1.0 (breakpoints, background Run, Pause/Stop); v0.10.1 MARS-accurate pseudo-instructions, machine code, About dialog, unsaved-changes prompts; v0.11.0 console input syscalls and register/memory editing with right-click menus; v0.12.0 live error squiggles (including assembly inside C# strings), Edit menu (Find/Replace, Go to Line/Label, line editing), Messages navigation, and stability (error dialog, regression tests). The plan below is the original suggestion; remaining milestones shift accordingly.

## v0.9

Debugger/UI milestone:

- Memory Viewer 2.0
- Register Viewer 2.0
- changed-value highlighting
- machine-code column
- breakpoint basics

## v0.10

Assembler compatibility milestone:

- more instructions
- more pseudo-instructions
- directives
- better diagnostics

## v0.11

Project milestone:

- multi-file assembly
- improved Explorer
- recent projects
- session restore

## v0.12

C# milestone:

- multiple C# files
- better runtime bridge
- Stop support
- error navigation

## v0.13

Educational milestone:

- stack viewer
- heap viewer
- instruction details
- symbols

## v0.14

Floating-point milestone:

- Coprocessor 1
- floating-point instructions
- floating-point syscalls

## v0.15

System milestone:

- exceptions
- Coprocessor 0
- MMIO

## v0.16

Polish milestone:

- settings
- shortcuts
- themes
- layout persistence

## v0.17

Documentation milestone:

- docs
- examples
- tutorial projects
- compatibility table

## v0.18

Distribution milestone:

- installer
- portable build
- update system
- crash recovery

## v0.19

Release candidate:

- bug fixing
- performance
- accessibility
- compatibility verification

## v1.0

Teacher-ready public release.

---

# Teacher Presentation Checklist

Before showing ASMForge to a teacher:

- [ ] clean installation
- [~] application opens quickly
- [~] no development/debug windows
- [~] no obvious unfinished placeholder text
- [x] version number visible
- [ ] sample project ready
- [x] assembly runs
- [x] breakpoint works
- [x] step works
- [x] backstep works
- [x] register change is visible
- [x] memory change is visible
- [x] machine code is visible
- [ ] stack example works
- [x] C# interop example works
- [x] invalid code produces a useful error
- [x] README is polished *(rewritten for v0.10.1; screenshots still to add)*
- [x] About dialog gives project description

---

# Suggested Teacher Demo Script

## 1. Introduce ASMForge

Explain:

> ASMForge is a MIPS development environment I built as a modern alternative to MARS. It includes an assembler, simulator, debugger, memory/register viewers, and C# interoperability.

## 2. Open a MIPS Project

Show:

- Project Explorer
- source editor
- syntax highlighting

## 3. Assemble

Show:

- source instruction
- pseudo-instruction expansion
- text address
- machine code

## 4. Debug

Set a breakpoint.

Run.

Step through several instructions.

Point out:

- PC
- changed register
- changed memory

## 5. Memory

Show:

- Data
- Heap
- Stack
- Hex
- Decimal
- Binary
- ASCII

## 6. Function Call

Step through:

```asm
jal function
```

Show:

- `$ra`
- stack
- return

## 7. Backstep

Step backward and show registers/memory revert.

## 8. C# Interop

Open `Program.cs`.

Show C# creating:

```csharp
var mips = new ASMForgeRuntime();
```

Run it.

Show C# retrieving a MIPS register value.

## 9. Error Handling

Introduce a deliberate invalid instruction.

Show the useful diagnostic.

## 10. Finish

Explain future possibilities:

- floating point
- MMIO
- pipeline visualization
- RISC-V
- classroom tools

---

# Definition of “Teacher-Ready”

ASMForge does not need every possible MARS feature before it is worth showing.

A strong teacher-ready build should:

1. look intentional and polished
2. reliably run common class assignments
3. clearly visualize CPU state
4. have useful debugging controls
5. explain errors
6. demonstrate something unique beyond MARS
7. include documentation
8. include a polished example project

The strongest unique feature is likely:

> **C# controlling and inspecting a simulated MIPS machine through ASMForgeRuntime.**

That is a compelling feature to demonstrate because it goes beyond simply cloning MARS.

---

# Long-Term Success Criteria

ASMForge can be considered mature when a student can:

- download it
- create a project
- write a normal MIPS assignment
- assemble it
- debug it
- inspect memory
- inspect registers
- inspect machine code
- understand errors
- compare source with generated basic instructions
- use stack/heap visualization
- save and reopen their work
- optionally control the simulator from C#

without needing another program.

---

# Final Goal

ASMForge should eventually feel like a combination of:

- MARS
- a modern IDE
- a visual debugger
- an educational CPU tool
- a programmable MIPS simulator

rather than simply being another assembly text editor.

The project is complete enough for a public 1.0 when it is stable, understandable, visually polished, and genuinely useful for completing and debugging real MIPS coursework.
