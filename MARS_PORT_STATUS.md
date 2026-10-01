# MARS Port Status - ASMForge v0.8.1

## Working / ported
- Core integer MIPS simulation from the v0.8 checkpoint.
- Real MIPS text/data addresses.
- Data directives and labels.
- MARS-style data alignment.
- Common pseudo-instructions.
- Core load/store, arithmetic, branch/jump, HI/LO behavior.
- MARS-compatible syscall subset already present in v0.8.
- Live Data/Heap/Stack memory viewer.
- Memory display format switching: hex, signed decimal, unsigned decimal, binary, ASCII.
- Resizable memory columns.
- Public C# `ASMForgeRuntime` API.
- Built-in Roslyn compilation/execution of C# project files.
- Project Explorer now includes generated C# project files.
- Backstep (v0.9, pending build verification): per-step change record restores registers, HI/LO, memory, PC, heap break and console output.

## Still outstanding for broader MARS compatibility
- Full Coprocessor 1 floating-point implementation.
- Coprocessor 0 exception/status model.
- Remaining MARS syscalls.
- Full macro / include compatibility.
- Memory-mapped I/O.
- Delayed-branch configuration.
- Full machine-code encoding/decoding in the Text Segment Code column.
- Alternate MARS memory configurations.
- Full multi-file MIPS linking semantics.
