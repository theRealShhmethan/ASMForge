# ASMForge MARS Compatibility Port Status

This checkpoint upgrades the original v0.7.2 execution core from a small instruction-list emulator into a MARS-style source-level MIPS engine with real virtual addresses and initialized data memory.

## Implemented in this checkpoint

- Real MIPS text/data addresses (`0x00400000`, `0x10010000`)
- MARS-style `$gp` and `$sp` initialization
- Text and data symbols
- Entry at `main` when present
- Sparse byte-addressable memory
- `.text`, `.data`, `.ktext`, `.kdata`, `.globl`, `.set`, `.eqv`, `.extern`
- `.byte`, `.half`, `.word`, `.space`, `.align`, `.ascii`, `.asciiz`, `.float`, `.double`
- String escape parsing and comment handling inside strings
- Source label resolution and label+offset operands
- Common MIPS32 integer ALU, shifts, compares, HI/LO, multiply/divide, loads/stores, branches and jumps
- Common MARS pseudo-instructions including `li`, `la`, `move`, `neg`, `not`, `rem`, unconditional/zero branches and relational branch/set forms
- MARS-style signed overflow behavior for `add`, `addi`, and `sub`
- MARS-style divide-by-zero behavior (HI/LO unchanged; no arithmetic exception)
- Syscalls 1, 4, 9, 10, 11, 17, 34, 35, and 36
- Text segment UI now displays each assembled instruction's actual address
- Autocomplete expanded for newly supported instructions/directives
- Core tests added for data labels, strings, label-based loads, and branches

## Still to port from full MARS

The full MARS source is much larger than the original ASMForge core. These areas remain separate follow-up work rather than being silently treated as supported:

- Coprocessor 1 / floating-point registers and FP instructions
- Coprocessor 0, traps, exception vectors, and `eret`
- Remaining trap instructions
- Full MARS macro processor and `.include`
- Full file/input/dialog/MIDI/random/time syscall set
- Memory-mapped keyboard/display devices
- Delayed-branch option
- Full machine-code encoding/decoding and binary/hex columns
- Backstep state restoration
- Breakpoint manager
- Self-modifying code mode
- Alternate MARS memory configurations
- Multi-file linking/global symbol semantics

The engine has been structured so these can be added in Core without rewriting the Avalonia editor.
