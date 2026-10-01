# Manual test program for breakpoints, Pause/Stop and Run to Cursor.
.data
newline: .asciiz "\n"

.text
main:
    li   $t0, 0              # loop counter

loop:
    addi $t0, $t0, 1         # <- set a breakpoint here: Run stops once per iteration
    move $a0, $t0
    li   $v0, 1
    syscall                  # prints the counter
    la   $a0, newline
    li   $v0, 4
    syscall
    blt  $t0, 5, loop

    li   $t1, 100            # <- put the cursor here and use Run to Cursor (Ctrl+F10)

# Infinite loop: Run keeps going until you press Pause (F6) or Stop (Shift+F5).
