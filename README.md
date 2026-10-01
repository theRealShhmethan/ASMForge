# ASMForge v0.5.1

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


## v0.5 build fix
- Added the Avalonia.Controls.DataGrid package required by the Text Segment view.
- Added the DataGrid Fluent theme styles.
- Ensured ASMForge.App builds as a WinExe.
- Disabled compiled bindings for the current code-behind-driven prototype UI.


## v0.5 Files & Projects
Adds New/Open/Save, Ctrl+N creation dialog, ASM/C# file creation, ASMForge project creation, editor tabs, and a project/folder explorer. C# files are editable but C# execution is intentionally not implemented yet.
