# ASMForge v0.3

ASMForge is a modern learning-focused MIPS assembly simulator/IDE being built in C#/.NET with Avalonia.

## v0.3 additions
- System, Light, and Dark themes (System is default).
- Current source-line selection while stepping.
- Basic/expanded instruction pane with PC addresses and current instruction selection.
- Source-to-basic mapping retained by the assembler.
- `rem` expands to `div` + `mfhi`; `move` expands to `addu`; small `li` expands to `addiu`.
- HI/LO registers are displayed and `div`, `mfhi`, and `mflo` execute in the simulator.
- Register display modes: Hex, Signed Decimal, Unsigned Decimal, Binary, ASCII.

## Safety
All MIPS registers and memory are simulated data structures. Guest MIPS addresses are never treated as host-process memory addresses.

## Compatibility direction
The included `reference/Mars.jar` is the MARS compatibility reference. v0.3 is not full MARS parity yet; unsupported instructions report a diagnostic instead of silently behaving incorrectly.
