.data
byteValue: .byte 65
halfValue: .half 1234
wordValue: .word 0x12345678
message: .asciiz "Memory viewer works!"

.text
main:
    # Data segment write: change wordValue to 42.
    li $t0, 42
    sw $t0, wordValue

    # Heap write: syscall 9 returns heap address in $v0.
    li $a0, 16
    li $v0, 9
    syscall
    move $t1, $v0
    li $t2, 0x11223344
    sw $t2, 0($t1)

    # Stack write: visible in the Stack memory view.
    addi $sp, $sp, -16
    li $t3, 99
    sw $t3, 0($sp)

    li $v0, 10
    syscall
