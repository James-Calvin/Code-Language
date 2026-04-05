# Bytecode Specification (draft)

Version: 0.9 (2026-04-05)

## File format
- Header: "CODE" ASCII (4 bytes) + version byte (0x05) + int32 codeSize + int32 debugCount.
- Encoding: little-endian integers.
- Layout: header, then `codeSize` bytes of opcodes/operands, followed by `debugCount` debug entries (ip, line, column; each int32).
- Produced files should use the `.bytecode` extension.

## Library artifact container (`.codelib`) (baseline)
- Format: JSON container with schema version 1.
- Required fields:
  - `schemaVersion`: `1`
  - `name`: package name
  - `version`: package version
  - `kind`: package kind (currently `library`)
  - `target`: compile target (`vm-native` or `vm-web`)
  - `entry`: package entry module path
  - `bytecode`: base64-encoded `.bytecode` payload
- Optional fields:
  - `exports`: export-name to module-path map
  - `requiredCapabilities`: capability list (`std.*`, `engine.*`)
- Artifact filename convention: `<package>-<version>-<target>.codelib`.
- VM execution/disassembly tools may consume `.codelib` by decoding embedded `bytecode`.

## Stack conventions
- Operand stack stores numeric values as `int`, `long`, or `double` (numeric ops coerce to double math); strings and objects are boxed.
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
| 0x16 | NEW_ARRAY | int32 count | -count+1 | Pop N values, build array (preserve order), push array |
| 0x17 | ARRAY_LENGTH | — | 0 | pop array, push length |
| 0x18 | ARRAY_GET | — | -1 | pop index, pop array, push element; throws on OOB |
| 0x19 | NEW_ARRAY_N | — | -1 | pop size N, allocate array of length N filled with 0 |
| 0x1A | OPTIONAL_NONE | — | +1 | push optional-none sentinel |
| 0x1B | OPTIONAL_HAS | — | 0 | pop optional, push 1 if present else 0 |
| 0x1C | OPTIONAL_VALUE | — | 0 | pop optional, push value or panic if none |
| 0x1D | OPTIONAL_OR | — | -1 | pop fallback, pop optional, push optional-or-fallback |
| 0x1E | ARRAY_SET | — | -2 | pop value, index, array; set element; push value |
| 0x1F | NEW_OBJECT | int32 length, UTF-8 type name | +1 | create VM object instance |
| 0x20 | GET_FIELD | int32 length, UTF-8 field name | 0 | pop object, push field value; throws if unset/missing |
| 0x21 | SET_FIELD | int32 length, UTF-8 field name | -1 | pop value, pop object, assign field, push value |
| 0x22 | GET_TYPE_NAME | — | 0 | pop object, push runtime object type name string |
| 0x23 | INTERFACE_CALL | int32 explicitArgCount, int32 entryCount, repeated `(string typeName, int32 target, int32 locals)` | -explicitArgCount-1+1 | Pop args and target object, dispatch by runtime object type to mapped method target, push return value on RET |
| 0x24 | MOD | — | -1 | pop a,b → push (a%b), throws on modulo-by-zero |
| 0x25 | TIME_UNIX_MS | — | +1 | push current Unix wall-clock milliseconds |
| 0x26 | TIME_UNIX_US | — | +1 | push current Unix wall-clock microseconds |
| 0x27 | TIME_MONO_NS | — | +1 | push process-relative monotonic nanoseconds |
| 0x28 | TIME_MONO_TICKS | — | +1 | push runtime monotonic tick counter |
| 0x29 | TIME_MONO_TICKS_PER_SECOND | — | +1 | push monotonic tick frequency |
| 0x2A | HOST_CALL | string symbol, int32 argc | -argc+1 | invoke host binding by symbol; pushes one return value (void-like calls return 0) |
| 0x2B | ARRAY_APPEND | — | -1 | pop value, pop array, append element, push 0 |
| 0x2C | ARRAY_REMOVE_AT | — | -2+1 | pop index, pop array, remove element, push 0 |
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
- Arrays are stored as VM-managed lists; NEW_ARRAY pops pre-pushed elements, NEW_ARRAY_N allocates default-filled arrays of length N, ARRAY_LENGTH/GET operate on them (GET pops index then array).
- ARRAY_SET pops value, index, array; writes in place; returns the value.
- ARRAY_APPEND pops value then array, mutates the array in place, and returns `0`.
- ARRAY_REMOVE_AT pops index then array, removes the indexed element in place, and returns `0`.
- Objects are stored as VM-managed instances with a type name and field dictionary; field access is name-based via GET_FIELD/SET_FIELD.
- Object methods currently lower to regular CALL sites with implicit `this` prepended to explicit arguments; overload choice is resolved at compile time.
- Interface declarations and `implement` mappings remain compile-time metadata in v0.8; interface-typed calls lower to `INTERFACE_CALL` dispatch tables that map runtime type names to method targets. Dispatch tables may be empty when no implementers are present in the current compile; runtime then raises a missing-implementation error if the call executes.
- VM caches decoded `INTERFACE_CALL` tables by call-site IP to avoid reparsing dispatch metadata on hot paths.
- Module imports/exports/package declarations are compile-time only; the linker flattens a module graph into one bytecode unit before VM execution.
- Package lockfile resolution may reference either manifest paths or `.codelib` artifacts; when a valid artifact exists for target/version, resolver prefers `.codelib`.
- Current compiler lowering routes host-facing language features through `HOST_CALL` symbols:
  - print/time: `standard.input_output.print`, `std.time.*`
  - native-only APIs: `standard.input_output.read_line`, `std.time.sleep_ms`
  - engine stubs: `engine.window.*`, `engine.input.*`, `engine.gfx.*`
  - generated web-app scene runtime: `engine.input.key_down_scene`, `engine.window.camera_view_*_scene`, `engine.window.camera_safe_*_scene`, `engine.window.screen_width_scene`, `engine.window.screen_height_scene`, `engine.gfx.clear_scene`, `engine.gfx.draw_rectangle_scene`, `engine.gfx.draw_rectangle_outline_scene`, `engine.gfx.draw_line_scene`, `engine.gfx.draw_circle_scene`, `engine.gfx.draw_circle_outline_scene`, `engine.gfx.draw_polygon_scene`, `engine.gfx.draw_polygon_outline_scene`, `engine.gfx.draw_text_scene`, `engine.gfx.draw_image_scene`, `engine.gfx.draw_sprite_scene`
- Runtime hosts still accept legacy migration aliases such as `std.io.*` and scene `draw_rect`-era symbols so previously compiled artifacts continue to run during the rename window.
- Runtime host mode (`vm-native`/`vm-web`) selects the host binding table used by `HOST_CALL`; missing symbol/arity mismatches raise `HostBindingError`.
- Native-only symbols are expected to raise target-specific `HostBindingError` diagnostics when executed on `vm-web`.

