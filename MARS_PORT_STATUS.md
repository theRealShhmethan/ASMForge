# MARS Port Status - ASMForge v0.12.0

## Working / ported
- Core integer MIPS simulation from the v0.8 checkpoint.
- Real MIPS text/data addresses.
- Data directives and labels.
- MARS-style data alignment.
- Pseudo-instructions expanded exactly as MARS does, driven by MARS's own PseudoOps.txt (integer forms; floating-point forms pending). Text addresses match MARS.
- Core load/store, arithmetic, branch/jump, HI/LO behavior.
- Syscalls 1, 4, 5, 8, 9, 10, 11, 12, 17, 30, 34, 35, 36, 40, 41, 42 (console input via the Run I/O input box).
- Live Data/Heap/Stack memory viewer.
- Memory display format switching: hex, signed decimal, unsigned decimal, binary, ASCII.
- Resizable memory columns.
- Public C# `ASMForgeRuntime` API.
- Built-in Roslyn compilation/execution of C# project files.
- Project Explorer now includes generated C# project files.
- Backstep (v0.9): per-step change record restores registers, HI/LO, memory, PC, heap break and console output.

## Still outstanding for broader MARS compatibility
- Full Coprocessor 1 floating-point implementation.
- Coprocessor 0 exception/status model.
- Remaining MARS syscalls (floating point 2/3/6/7, files 13-16, sleep 32, MIDI 31/33, dialogs 50-59, random float/double 43/44).
- Full macro / include compatibility.
- Memory-mapped I/O.
- Delayed-branch configuration.
- Machine-code decoding (disassembling words back to instructions); encoding is done.
- Alternate MARS memory configurations.
- Full multi-file MIPS linking semantics.
