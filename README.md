# ASMForge v0.2

ASMForge is a learning-focused assembly IDE and simulator. The current milestone provides a simulated MIPS CPU, isolated simulated memory, a small MIPS instruction subset, console syscalls, stepping/running, and switchable register display formats.

## Register display modes
- Hex
- Signed Decimal
- Unsigned Decimal
- Binary
- ASCII (low byte, with escaped control characters)

Changing display mode never changes the stored 32-bit register value.

## Safety model
MIPS registers and addresses exist only inside ASMForge's simulated machine. Guest MIPS memory accesses are validated against ASMForge's simulated memory and do not directly access host CPU registers or arbitrary host RAM.

## Projects
- `ASMForge.App` - Avalonia desktop UI
- `ASMForge.Core` - assembler, CPU, execution, simulated memory
- `ASMForge.Projects` - foundation for future multi-file projects and C# integration
- `ASMForge.Core.Tests` - core tests

## Compatibility reference
`reference/Mars.jar` is retained as the MARS behavior/reference artifact supplied for development. Preserve applicable upstream license notices when redistributing upstream MARS material.
