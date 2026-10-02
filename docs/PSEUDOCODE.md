# ASMForge Pseudocode

ASMForge can translate a small C-like pseudocode into commented MIPS assembly. The translation is
deterministic (no AI): the same pseudocode always produces the same assembly.

- **Tools > Pseudocode Generator** opens a window with pseudocode on the left and the generated assembly on the
  right, updated as you type. Use **Open as New Tab**, **Insert into Current File** or **Copy Assembly**.
- **`.pseudo` files** (File > New > Pseudocode File) get syntax colors and live error squiggles.
  **F3** generates `name.asm` next to the file and opens it; **F5** generates and runs it.
  An existing `.asm` that was not created by the generator is never overwritten.

## Language

```c
// Variables are 32-bit integers.
int x = 5;
int a[10];               // array of 10, all 0
int b[3] = {1, 2, 3};    // array with starting values
int c = 1, d = 2;        // several at once

// Assignment
x = x + 1;
x += 2;                  // also -=  *=  /=  %=
x++;  x--;               // as statements only
a[i] = 7;

// Decisions
if (x > 0 && y != 0) { ... } else if (x == 0) { ... } else { ... }

// Loops
while (x < 10) { ... }
do { ... } while (x < 10);
for (int i = 0; i < 10; i++) { ... }
break;  continue;

// Output and input
print(x);                // integer
print("text\n");         // text (escapes: \n \t \\ \" \0)
print("x = ", x, "\n");  // several values
print('c');              // a character literal
printChar(x);            // the character whose code is x
int n = readInt();
int k = readChar();
exit();                  // ends the program (added automatically at the end)
```

- **Operators:** `+ - * / %`, `== != < <= > >=`, `&& || !`, parentheses. Precedence follows C, and `&&` / `||`
  short-circuit (the right side is only evaluated when needed).
- **Values:** decimal or hex numbers (`0x1F`), character literals (`'a'`, `'\n'`), `true` (1) and `false` (0).
- **Comments:** `// ...`, `/* ... */` and `# ...`. Code pasted from an assembly comment block, where every line
  starts with `#`, is accepted as-is.

## Generated code

The output is written in the style of a hand-written course assignment:

- `.data` first (arrays, then strings), then `.text` and `.globl main`, then the **whole pseudocode as a `#` comment
  block**, a `# Registers: $s0 = i, ...` note, and `main:`.
- **Variables live in `$s0`-`$s7`** in the order they are declared; beyond eight, the rest are `.data` words.
  Arrays are `.data` labels (`.word` values or `.space`). Expressions use `$t0`-`$t9`.
- **Strings are named after their text**: `"FizzBuzz\n"` becomes `fizzBuzz`, `"\n"` becomes `newline`.
- **Labels are named after the construct**: `loop`/`endLoop`, `forLoop`/`forNext`/`endFor`,
  `doLoop`/`doCondition`/`endDo`, `else`/`endIf`; later ones are numbered (`loop2`, `endIf2`).
  An array whose name is already used as a label (such as `main`) is renamed `var_main`.
- **Short trailing comments** explain each step: `bgt $s0, 100, endLoop  # if i > 100 → endLoop`,
  `addi $s0, $s0, 1  # i++`, `syscall  # Print "Fizz\n"`, `# Select exit syscall`, `# Exit program`.
- Division and remainder use MARS's `div`/`rem` pseudo-instructions, which stop with "Division by zero" at run time.

## Not yet supported

Functions, strings as variables, and floating point.
