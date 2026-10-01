# ASMForge v0.6.6 - Diagnostics Build

## v0.6.6 editor-rendering fix

- Adds the required AvaloniaEdit Fluent theme resource so the editor template actually renders.
- Keeps runtime editor diagnostics in Visual Studio Debug Output and `%LOCALAPPDATA%\ASMForge\Logs`.
- Adds **Settings > Syntax Highlighting** so highlighting can be toggled without rebuilding.
- Removes duplicate diagnostic lines in Visual Studio Output.
- Keeps line numbers, multi-line Tab/Shift+Tab indentation, file tabs, project explorer filtering, and Run I/O default behavior.


This build adds diagnostic logging so editor/layout failures can be inspected instead of guessed.

## Logs
When run from Visual Studio with F5, look at **View > Output** and choose **Debug**.
ASMForge messages start with `[ASMForge]`.

A persistent log is also written to:

`%LOCALAPPDATA%\\ASMForge\\Logs\\ASMForge-YYYY-MM-DD.log`

Useful lines include editor creation, selected tab, editor bounds, visibility, parent/visual parent, text length, and line-number state.

If the editor is blank, open/switch the affected file, wait a moment, then copy the `[ASMForge]` lines from Visual Studio Output or send the log file.
