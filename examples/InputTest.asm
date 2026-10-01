# Manual test program for console input (syscalls 5, 8, 12) and random numbers (40-42).
.data
askName:  .asciiz "What is your name? "
askAge:   .asciiz "How old are you? "
askChar:  .asciiz "Pick a letter: "
hello:    .asciiz "Hello, "
nextYear: .asciiz "Next year you will be "
letter:   .asciiz "\nYou picked: "
dice:     .asciiz "\nDice roll: "
name:     .space 32

.text
main:
    la   $a0, askName        # read string (syscall 8) into name, max 32 bytes
    li   $v0, 4
    syscall
    la   $a0, name
    li   $a1, 32
    li   $v0, 8
    syscall

    la   $a0, askAge         # read integer (syscall 5)
    li   $v0, 4
    syscall
    li   $v0, 5
    syscall
    move $s0, $v0

    la   $a0, askChar        # read character (syscall 12)
    li   $v0, 4
    syscall
    li   $v0, 12
    syscall
    move $s1, $v0

    la   $a0, hello          # "Hello, <name>" (name keeps its newline)
    li   $v0, 4
    syscall
    la   $a0, name
    li   $v0, 4
    syscall

    la   $a0, nextYear       # age + 1
    li   $v0, 4
    syscall
    addi $a0, $s0, 1
    li   $v0, 1
    syscall

    la   $a0, letter
    li   $v0, 4
    syscall
    move $a0, $s1
    li   $v0, 11
    syscall

    li   $a0, 0              # random 1..6 (syscall 42 gives 0..5)
    li   $a1, 6
    li   $v0, 42
    syscall
    addi $s2, $a0, 1
    la   $a0, dice
    li   $v0, 4
    syscall
    move $a0, $s2
    li   $v0, 1
    syscall

    li   $v0, 10
    syscall
