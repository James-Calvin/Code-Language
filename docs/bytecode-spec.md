# Bytecode Specification (draft)

Version: 0.9 (2026-04-05)

## File format
- Header: `CODE` ASCII (4 bytes) + version byte (`0x05`) + int32 `codeSize` + int32 `debugCount`.
- Encoding: little-endian integers.
- Layout: header, then `codeSize` bytes of opcodes/operands, followed by `debugCount` debug entries (`ip`, `line`, `column`; each int32).
- Produced files should use the `.bytecode` extension.

## Library artifact container (`.codelib`) (baseline)
- Format: JSON container with schema version `1`.
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
- Operand stack stores numeric values as `int`, `long`, or `double` (numeric ops coerce to double math); strings and runtime objects are boxed.
- Locals are indexed slots separate from the operand stack; they auto-grow on demand. Functions record a high-water mark for frame size.
- Call frames: `CALL` creates a new locals array sized by the callee; `RET` restores previous locals and IP, leaving the return value on the operand stack.

## Opcodes
| Byte | Name | Operands | Stack effect | Notes |
| ---- | ---- | -------- | ------------ | ----- |
| 0x01 | `PUSH_CONST` | int32 | +1 | Push 32-bit signed int as double |
| 0x02 | `ADD` | - | -1 | Pop `a`, `b`; push `a + b` or string concat if either is string |
| 0x03 | `SUB` | - | -1 | Pop `a`, `b`; push `a - b` |
| 0x04 | `MUL` | - | -1 | Pop `a`, `b`; push `a * b` |
| 0x05 | `DIV` | - | -1 | Pop `a`, `b`; push `a / b`; throws on divide-by-zero |
| 0x06 | `PRINT` | - | -1 | Pop value, write to output with newline |
| 0x07 | `DUP` | - | +1 | Duplicate top of stack |
| 0x08 | `SWAP` | - | 0 | Swap top two values |
| 0x09 | `POP` | - | -1 | Discard top value |
| 0x0A | `JUMP` | int32 target | 0 | Set IP to absolute byte offset |
| 0x0B | `JUMP_IF_ZERO` | int32 target | -1 | Pop test; jump if zero |
| 0x0C | `JUMP_IF_NOT_ZERO` | int32 target | -1 | Pop test; jump if non-zero |
| 0x0D | `LOAD` | int32 slot | +1 | Push `locals[slot]` |
| 0x0E | `STORE` | int32 slot | -1 | Pop value into `locals[slot]` |
| 0x0F | `EQ` | - | -1 | Numeric compare if both numeric; otherwise reference/value equality; pushes `1` or `0` |
| 0x10 | `LT` | - | -1 | Push `1` if `a < b`, else `0` |
| 0x11 | `GT` | - | -1 | Push `1` if `a > b`, else `0` |
| 0x12 | `CALL` | int32 target, int32 argc, int32 locals | -argc+1 | Pop args, create frame, jump; pushes return value on `RET` |
| 0x13 | `RET` | - | -1 | Pop return value, restore caller frame, push return value |
| 0x14 | `PUSH_STRING` | int32 length, UTF-8 bytes | +1 | Push string literal |
| 0x15 | `THROW_ERROR` | - | -1 | Pop message, raise `VmError` of type `UserError` |
| 0x16 | `NEW_ARRAY` | int32 count | -count+1 | Pop N values, preserve order, push array |
| 0x17 | `ARRAY_LENGTH` | - | 0 | Pop array or built-in collection, push length |
| 0x18 | `ARRAY_GET` | - | -1 | Pop index, pop array, push element; throws on out-of-range |
| 0x19 | `NEW_ARRAY_N` | - | -1 | Pop size N, allocate array of length N filled with `0` |
| 0x1A | `OPTIONAL_NONE` | - | +1 | Push optional-none sentinel |
| 0x1B | `OPTIONAL_HAS` | - | 0 | Pop optional, push `1` if present else `0` |
| 0x1C | `OPTIONAL_VALUE` | - | 0 | Pop optional, push value or throw if none |
| 0x1D | `OPTIONAL_OR` | - | -1 | Pop fallback, pop optional, push value-or-fallback |
| 0x1E | `ARRAY_SET` | - | -2 | Pop value, index, array; write element; push value |
| 0x1F | `NEW_OBJECT` | int32 length, UTF-8 type name | +1 | Create VM object instance |
| 0x20 | `GET_FIELD` | int32 length, UTF-8 field name | 0 | Pop object, push field value; throws if unset/missing |
| 0x21 | `SET_FIELD` | int32 length, UTF-8 field name | -1 | Pop value, pop object, assign field, push value |
| 0x22 | `GET_TYPE_NAME` | - | 0 | Pop object, push runtime object type name string |
| 0x23 | `INTERFACE_CALL` | int32 explicitArgCount, int32 entryCount, repeated `(string typeName, int32 target, int32 locals)` | -explicitArgCount-1+1 | Pop args and target object, dispatch by runtime object type |
| 0x24 | `MOD` | - | -1 | Pop `a`, `b`; push `a % b`; throws on modulo-by-zero |
| 0x25 | `TIME_UNIX_MS` | - | +1 | Push current Unix wall-clock milliseconds |
| 0x26 | `TIME_UNIX_US` | - | +1 | Push current Unix wall-clock microseconds |
| 0x27 | `TIME_MONO_NS` | - | +1 | Push process-relative monotonic nanoseconds |
| 0x28 | `TIME_MONO_TICKS` | - | +1 | Push runtime monotonic tick counter |
| 0x29 | `TIME_MONO_TICKS_PER_SECOND` | - | +1 | Push monotonic tick frequency |
| 0x2A | `HOST_CALL` | string symbol, int32 argc | -argc+1 | Invoke host binding by symbol; pushes one return value |
| 0x2B | `ARRAY_APPEND` | - | -1 | Pop value, pop array, append element, push `0` |
| 0x2C | `ARRAY_REMOVE_AT` | - | -2+1 | Pop index, pop array, remove element, push `0` |
| 0x2D | `NEW_MAP` | - | +1 | Push empty map |
| 0x2E | `MAP_GET` | - | -1 | Pop key, pop map, push value; throws if key missing |
| 0x2F | `MAP_SET` | - | -2 | Pop value, pop key, pop map; assign entry; push value |
| 0x30 | `MAP_CONTAINS` | - | -1 | Pop key, pop map, push `1` if present else `0` |
| 0x31 | `MAP_REMOVE` | - | -2+1 | Pop key, pop map, remove entry if present, push `0` |
| 0x32 | `NEW_SET` | - | +1 | Push empty set |
| 0x33 | `SET_ADD` | - | -2+1 | Pop value, pop set, add value, push `0` |
| 0x34 | `SET_CONTAINS` | - | -1 | Pop value, pop set, push `1` if present else `0` |
| 0x35 | `SET_REMOVE` | - | -2+1 | Pop value, pop set, remove value if present, push `0` |
| 0x36 | `NEW_QUEUE` | - | +1 | Push empty queue |
| 0x37 | `QUEUE_ENQUEUE` | - | -2+1 | Pop value, pop queue, enqueue value, push `0` |
| 0x38 | `QUEUE_DEQUEUE` | - | 0 | Pop queue, push dequeued value; throws if empty |
| 0x39 | `QUEUE_PEEK` | - | 0 | Pop queue, push next value; throws if empty |
| 0x3A | `NEW_STACK` | - | +1 | Push empty stack |
| 0x3B | `STACK_PUSH` | - | -2+1 | Pop value, pop stack, push value, push `0` |
| 0x3C | `STACK_POP` | - | 0 | Pop stack, push popped value; throws if empty |
| 0x3D | `STACK_PEEK` | - | 0 | Pop stack, push top value; throws if empty |
| 0xFF | `HALT` | - | 0 | Stop execution |

## Planned additions
- Constant pool for strings and other literals
- Structured exception objects beyond `UserError` / `RuntimeError`
- Magic header evolution and validation rules

## Notes
- All operands are 4-byte little-endian offsets or indices.
- Comparisons return `1.0` for true and `0.0` for false; logical `and` / `or` short-circuit in codegen.
- Locals grow dynamically when `STORE` / `LOAD` targets exceed current length; functions track max locals for `CALL` frame sizing.
- Debug entries map instruction-pointer offsets back to source line/column for runtime stack traces.
- Arrays are VM-managed lists. `NEW_ARRAY` pops pre-pushed elements, `NEW_ARRAY_N` allocates default-filled arrays, `ARRAY_GET` / `ARRAY_SET` index them, and `ARRAY_APPEND` / `ARRAY_REMOVE_AT` mutate them in place.
- `ARRAY_LENGTH` also reports the size of VM-managed `map`, `set`, `queue`, and `stack` values.
- Maps and sets use VM-managed keyed containers. `MAP_GET` throws on missing keys. `MAP_CONTAINS` / `SET_CONTAINS` return `1` or `0`. Remove operations are no-ops when the entry is absent.
- Queues and stacks use VM-managed containers with empty-checking on `QUEUE_DEQUEUE` / `QUEUE_PEEK` / `STACK_POP` / `STACK_PEEK`.
- Objects are stored as VM-managed instances with a type name and field dictionary; field access is name-based via `GET_FIELD` / `SET_FIELD`.
- Object methods currently lower to regular `CALL` sites with implicit `this` prepended to explicit arguments; overload choice is resolved at compile time.
- Interface declarations and `implement` mappings remain compile-time metadata; interface-typed calls lower to `INTERFACE_CALL` dispatch tables that map runtime type names to method targets.
- VM caches decoded `INTERFACE_CALL` tables by call-site IP to avoid reparsing dispatch metadata on hot paths.
- Module imports/exports/package declarations are compile-time only; the linker flattens a module graph into one bytecode unit before VM execution.
- Package lockfile resolution may reference either manifest paths or `.codelib` artifacts; when a valid artifact exists for target/version, resolver prefers `.codelib`.
- Current compiler lowering routes host-facing language features through `HOST_CALL` symbols:
  - print/time/math: `standard.input_output.print`, `std.time.*`, `std.math.*`
  - native-only APIs: `standard.input_output.read_line`, `std.time.sleep_ms`
  - engine stubs: `engine.window.*`, `engine.input.*`, `engine.gfx.*`
  - generated web-app scene runtime: `engine.input.key_down_scene`, `engine.window.camera_view_*_scene`, `engine.window.camera_safe_*_scene`, `engine.window.screen_width_scene`, `engine.window.screen_height_scene`, `engine.gfx.clear_scene`, `engine.gfx.draw_rectangle_scene`, `engine.gfx.draw_rectangle_outline_scene`, `engine.gfx.draw_line_scene`, `engine.gfx.draw_circle_scene`, `engine.gfx.draw_circle_outline_scene`, `engine.gfx.draw_polygon_scene`, `engine.gfx.draw_polygon_outline_scene`, `engine.gfx.draw_text_scene`, `engine.gfx.draw_image_scene`, `engine.gfx.draw_sprite_scene`
- Runtime hosts still accept legacy migration aliases such as `std.io.*` and scene `draw_rect`-era symbols so previously compiled artifacts continue to run during the rename window.
- Runtime host mode (`vm-native` / `vm-web`) selects the host binding table used by `HOST_CALL`; missing symbol/arity mismatches raise `HostBindingError`.
- Native-only symbols are expected to raise target-specific `HostBindingError` diagnostics when executed on `vm-web`.
