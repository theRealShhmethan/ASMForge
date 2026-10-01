# Manual test program for Step Back and changed-value highlighting.
# Step (F10) through it and watch the Registers and Data Segment panels.
.data
value:   .word 0x11223344
message: .asciiz "Hello"

.text
main:
    li   $t0, 5              # $t0 highlights (was 0x00000000)
    li   $t1, 7              # $t1 highlights
    mult $t0, $t1            # HI and LO highlight (LO = 35)
    mflo $t2                 # $t2 = 35
    sw   $t2, value          # word at 0x10010000 highlights (was 0x11223344)
    sb   $t0, value          # only the +0 word highlights again (byte write)

    addi $sp, $sp, -4        # $sp highlights
    sw   $t1, 0($sp)         # Stack view: word at $sp highlights

    li   $a0, 16             # sbrk 16 bytes
    li   $v0, 9
    syscall                  # $v0 = 0x10040000 (heap start)

    la   $a0, message
    li   $v0, 4
    syscall                  # Run I/O shows "Hello"

    li   $t3, 1
    addi $t3, $t3, 2         # misaligns on purpose: $t3 = 3
    lw   $t4, 0($t3)         # runtime error: unaligned word address

    li   $v0, 10
    syscall
