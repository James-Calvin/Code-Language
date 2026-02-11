# Bytecode Specification (draft)

Version: 0.4 (2026-02-11)

## File format
- Header: "CODE" ASCII (4 bytes) + version byte (0x02) + int32 codeSize + int32 debugCount.
- Encoding: little-endian integers.
- Layout: header, then `codeSize` bytes of opcodes/operands, followed by `debugCount` debug entries (ip, line, column; each int32).
- Produced files should use the `.bytecode` extension.

## Stack conventions
- Operand stack uses IEEE-754 doubles (ints are widened when pushed); strings and objects are boxed.
- Locals are indexed slots separate from the operand stack; they auto-grow on demand. Functions record a high-water mark for frame size.
- Call frames: CALL creates a new locals array sized by the callee; RET restores previous locals and IP, leaving return value on the operand stack.

## Opcodes
| Byte | Name | Operands | Stack effect | Notes |
| ---- | ---- | -------- | ------------ | ----- |
| 0x01 | PUSH_CONST | int32 | +1 | Push 32-bit signed int as double |
| 0x02 | ADD | — | -1 | pop a,b → push (a+b) or string concat if either is string |
| 0x03 | SUB | — | -1 | pop a,b → push (a-b) |
| 0x04 | MUL | — | -1 | pop a,b → push (a*b) |
| 0x05 | DIV | — | -1 | pop a,b → push (a/b), throws on divide-by-zero |
| 0x06 | PRINT | — | -1 | pop value, write to output with newline (VmError prints as `Type: message at line:col`) |
| 0x07 | DUP | — | +1 | duplicate top of stack |
| 0x08 | SWAP | — | 0 | swap top two |
| 0x09 | POP | — | -1 | discard top |
| 0x0A | JUMP | int32 target | 0 | set IP to target (absolute byte offset) |
| 0x0B | JUMP_IF_ZERO | int32 target | -1 | pop test; jump if == 0 |
| 0x0C | JUMP_IF_NOT_ZERO | int32 target | -1 | pop test; jump if != 0 |
| 0x0D | LOAD | int32 slot | +1 | push locals[slot] (auto-resizes) |
| 0x0E | STORE | int32 slot | -1 | pop value → locals[slot] (auto-resizes) |
| 0x0F | EQ | — | -1 | numeric compare if both numeric; otherwise reference/value equality; pushes 1/0 |
| 0x10 | LT | — | -1 | push 1 if a<b else 0 |
| 0x11 | GT | — | -1 | push 1 if a>b else 0 |
| 0x12 | CALL | int32 target, int32 argc, int32 locals | -argc+1 | Pops argc args, creates frame with given locals size, jumps; pushes return value on RET |
| 0x13 | RET | — | -1 | pop return value, restore caller frame, push return value |
| 0x14 | PUSH_STRING | int32 length, UTF-8 bytes | +1 | Push string literal |
| 0x15 | THROW_ERROR | — | -1 | pop message, raise VmError (type `UserError`) with call stack |
| 0xFF | HALT | — | 0 | stop execution |

## Planned additions
- Constant pool for strings and other literals
- Structured exception objects beyond `UserError`/`RuntimeError`
- Magic header evolution and validation rules

## Notes
- All operands are 4-byte little-endian offsets/indices.
- Comparisons return 1.0 for true, 0.0 for false; logical `and/or` short-circuit in codegen.
- Locals array grows dynamically when STORE/LOAD targets exceed current length; functions track their max locals for CALL frame sizing.
- Debug entries map instruction pointer offsets (absolute byte positions) back to source line/column for runtime stack traces; entries are optional per instruction but recorded when available.
