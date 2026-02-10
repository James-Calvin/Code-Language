# Bytecode Specification (draft)

Version: 0.3 (2026-02-10)

## File format
- Header: "CODE" ASCII (4 bytes) + version byte (currently 0x01).
- Encoding: little-endian integers.
- Programs are linear byte streams of opcodes and operands after the header.
- Produced files should use the `.bytecode` extension.

## Stack conventions
- Operand stack uses IEEE-754 doubles (ints are widened when pushed).
- Locals are indexed slots separate from the operand stack; they auto-grow on demand.
- Call frames: CALL creates a new locals array; RET restores previous locals and IP, leaving return value on the operand stack.

## Opcodes
| Byte | Name | Operands | Stack effect | Notes |
| ---- | ---- | -------- | ------------ | ----- |
| 0x01 | PUSH_CONST | int32 | +1 | Push 32-bit signed int as double |
| 0x02 | ADD | — | -1 | pop a,b → push (a+b) |
| 0x03 | SUB | — | -1 | pop a,b → push (a-b) |
| 0x04 | MUL | — | -1 | pop a,b → push (a*b) |
| 0x05 | DIV | — | -1 | pop a,b → push (a/b), throws on divide-by-zero |
| 0x06 | PRINT | — | -1 | pop value, write to output with newline |
| 0x07 | DUP | — | +1 | duplicate top of stack |
| 0x08 | SWAP | — | 0 | swap top two |
| 0x09 | POP | — | -1 | discard top |
| 0x0A | JUMP | int32 target | 0 | set IP to target (byte offset, absolute from start of file) |
| 0x0B | JUMP_IF_ZERO | int32 target | -1 | pop test; jump if == 0 |
| 0x0C | JUMP_IF_NOT_ZERO | int32 target | -1 | pop test; jump if != 0 |
| 0x0D | LOAD | int32 slot | +1 | push locals[slot] (auto-resizes) |
| 0x0E | STORE | int32 slot | -1 | pop value → locals[slot] (auto-resizes) |
| 0x0F | EQ | — | -1 | push 1 if a==b else 0 |
| 0x10 | LT | — | -1 | push 1 if a<b else 0 |
| 0x11 | GT | — | -1 | push 1 if a>b else 0 |
| 0x12 | CALL | int32 target, int32 argc, int32 locals | -argc+1 | Pops argc args, creates frame with locals, jumps to target; pushes return value on RET |
| 0x13 | RET | — | -1 | pop return value, restore caller frame, push return value |
| 0xFF | HALT | — | 0 | stop execution |

## Planned additions
- Constant pool for strings and other literals
- Magic header evolution and validation rules

## Notes
- All operands are 4-byte little-endian offsets/indices.
- Comparisons return 1.0 for true, 0.0 for false.
- Locals array grows dynamically when STORE/LOAD targets exceed current length.
