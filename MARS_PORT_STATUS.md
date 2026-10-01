# MARS port status - ASMForge v0.8

## Working in this checkpoint

- Real MIPS text/data virtual addresses.
- Sparse simulated memory with Data, Heap, and Stack ranges.
- `.data` / `.text` labels and initialized data.
- `.byte`, `.half`, `.word`, `.space`, `.ascii`, `.asciiz`, `.float`, `.double`, `.align`, and `.eqv` support from the current port.
- MARS-style numeric data auto-alignment, including aligned label addresses.
- `.align 0` disables auto-alignment until the next data segment.
- Integer arithmetic, logical operations, shifts, comparisons, branches, jumps, HI/LO, common loads/stores, and the pseudo-instructions already present in the prior checkpoint.
- Syscalls 1, 4, 9, 10, 11, 17, 34, 35, and 36.
- Syscall 34 now outputs `0xXXXXXXXX` like MARS.
- Runtime memory viewer connected directly to `MipsMachine.Memory`.
- Data/Heap/Stack/Custom memory navigation and ASCII preview.
- Public C# `ASMForgeRuntime` host API.
- Correct New C# File template newlines and a working runtime-API example template.

## Not yet complete compared with MARS

- FPU / Coprocessor 1.
- Coprocessor 0 and exception/status behavior.
- Trap instructions and full exception dispatch.
- Remaining MARS syscalls and interactive input/file I/O.
- Macros and filesystem-aware `.include`.
- Full machine-code encoding/decoding.
- Memory-mapped I/O devices.
- Backstep state restoration.
- Configurable delayed branching.
- Alternate MARS memory configurations.
- Full multi-file assembly/linking semantics.

## Validation note

The source includes regression tests for auto-alignment, syscall 34 formatting, and the C# runtime bridge. This environment does not include the .NET SDK, so the tests could not be executed here; run `dotnet test` or build the solution locally before publishing a binary release.
