# ASMForge v0.6.3

Regression-fix release for the editor workspace.

## Fixed
- Each document tab now directly owns its AvaloniaEdit `TextEditor`.
- The editor is no longer hosted in a separate content area that could collapse to zero height.
- Editors stretch to fill the document workspace.
- Switching tabs switches the actual editor instance.
- Ctrl+W remains supported.
- Run I/O remains the default output tab.
- Project Explorer remains filtered to ASM/S/C# source files.
- Line-number setting and syntax coloring remain enabled.

Open `ASMForge.sln`, set `ASMForge.App` as the startup project, restore NuGet packages, and rebuild.
