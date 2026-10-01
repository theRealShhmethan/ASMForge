# ASMForge v0.6.2.2

ASMForge is a MARS-inspired MIPS learning IDE written in C#/.NET with Avalonia.

## v0.6.2.2 editor upgrade

- AvaloniaEdit-based source editor
- Settings > Show Line Numbers (persisted)
- ASM and C# syntax coloring with light/dark-aware palettes
- Multi-line Tab indentation and Shift+Tab outdent
- Project Explorer now lists only `.asm`, `.s`, and `.cs` source files
- New C# files use an ASMForge interop-ready template containing MIPS source for the upcoming simulated-MIPS bridge
- Existing EDIT / EXECUTE MARS-style workflow retained
- Hex / signed / unsigned / binary / ASCII register display retained
- System / Light / Dark themes retained

## Important

C# files are editable and receive the interop-ready template, but v0.6.2.2 does not yet execute C# or invoke the simulated MIPS engine from C#. The template intentionally does not claim native inline MIPS execution.


## v0.6.2 regression fixes
- Restored the visible AvaloniaEdit editor surface beneath file tabs.
- Added distinct Open File and Open Project toolbar actions.
- Open File now exposes Assembly, C#, and combined source filters.
- Ctrl+W closes the active file tab.
- Ctrl+O opens files; Ctrl+Shift+O opens projects/folders.
- Run I/O is the default output tab; Messages comes forward on errors.
- Corrected the Avalonia line-number menu checkbox declaration.
