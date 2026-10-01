# ASMForge v0.4

ASMForge is a C#/.NET + Avalonia learning-focused MIPS simulator inspired by the MARS workflow.

## v0.4 UI changes
- MARS-style **EDIT** and **EXECUTE** workspaces instead of the v0.3 split workspace.
- Assemble automatically switches from EDIT to EXECUTE.
- EXECUTE contains a **Text Segment** table with Address, Code, Basic, and Source columns.
- A **Data Segment** tab is reserved for the MARS-compatible memory/data implementation.
- Registers remain visible alongside execution and support Hex, signed/unsigned decimal, binary, and ASCII display.
- Bottom output area now has **Messages** and **Run I/O** tabs.
- System / Light / Dark theme selector remains available.
- Step follows the currently executing basic instruction and keeps source mapping.
- Back button is present in the MARS-like toolbar; full machine-state backstep is intentionally not faked yet.

## Current compatibility status
This is still a compatibility work in progress, not full MARS parity. Unsupported behavior should be implemented and tested rather than silently approximated.

Open `ASMForge.sln`, set `ASMForge.App` as the startup project, restore NuGet packages, and run.
