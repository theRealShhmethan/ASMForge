# ASMForge v0.6.4 - Diagnostics Build

This build adds diagnostic logging so editor/layout failures can be inspected instead of guessed.

## Logs
When run from Visual Studio with F5, look at **View > Output** and choose **Debug**.
ASMForge messages start with `[ASMForge]`.

A persistent log is also written to:

`%LOCALAPPDATA%\\ASMForge\\Logs\\ASMForge-YYYY-MM-DD.log`

Useful lines include editor creation, selected tab, editor bounds, visibility, parent/visual parent, text length, and line-number state.

If the editor is blank, open/switch the affected file, wait a moment, then copy the `[ASMForge]` lines from Visual Studio Output or send the log file.
