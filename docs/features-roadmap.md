# Code Language Features Roadmap

Legend:  
- `[x]` complete  
- `[!]` high priority  
- `[~]` medium priority  
- `[_]` low priority

This roadmap is implementation-truthful: items marked below are gaps from the current compiler/runtime surface, not draft syntax that already exists.

## Near-Term Gap Groups

### Language Gaps
1. Fallible-error propagation shorthand after the explicit `on error` model is exercised
2. `fallible<void, ErrorCode>` statement-level ergonomics
3. Numeric polish follow-ups: `integer64` / `whole64`, numeric literal suffixes, and exponent real literals
4. `foreach` over `map`, `set`, `queue`, and `stack`; planned map iteration should yield entry values

### Stdlib and Runtime Gaps
1. Broader standard-library modules after the core container/math baseline lands

### Web Runtime and Tooling Gaps
1. Advanced browser input/content handling beyond primary pointer input, diagnostics, copied assets, and basic asset-backed audio
2. Broader performance instrumentation if browser compositor/GPU timing becomes necessary
3. Optional explicit debug-output mode if normal `print` output ever needs to appear in-page again

### Engine and App Ergonomics Gaps
1. Refine the graphical app profile beyond its current web-entry slice toward fuller target-agnostic reuse
2. Broader engine wrapper packages; byte-style `rgba(byte, byte, byte, byte)` should build on the implemented `byte` / `whole8` surface now that `rgb(byte, byte, byte)` is implemented
3. Advanced browser input/content handling and fuller audio mixing
4. Longer-term GPU/backend work

## Status & Priority Table

|  Priority | Area | Item | Notes |
| --- | --- | --- | --- |
| `[x]` | Core language | Required type annotations; primitive numerics/string/boolean | Implemented |
| `[x]` | Core language | Variables, assignments, blocks | Implemented |
| `[x]` | Core language | Constants (`constant` declarations) | `constant Type name = value;` implemented with reassignment errors |
| `[x]` | Core language | Same-module global state | Top-level variables/constants are persistent module globals visible to functions, object/record field initializers, constructors, and methods in the same module; exported/imported globals remain deferred |
| `[x]` | Core language | Enhanced assignment operators | `+=`, `-=`, `*=`, `/=`, `%=` and unary `++`/`--` implemented for variables, object fields, array elements, and map entries |
| `[x]` | Core language | Arithmetic and comparisons | Implemented |
| `[x]` | Core language | Modulo operator (`%`) | Implemented in lexer/parser/type-checker/codegen/VM |
| `[x]` | Core language | Logical `and`/`or`/`not` (short-circuit) | Implemented |
| `[x]` | Core language | Boolean literals `true` / `false` | Implemented |
| `[x]` | Core language | String literals + interpolation; `+` concat | Implemented, including escaped literal interpolation braces with `\{` and `\}` |
| `[x]` | Core language | Interpolation expression parity | Interpolation now parses full expressions (member/index/call/ops) inside `{...}` |
| `[x]` | Core language | Control flow: `if/while/for` with mandatory `then` | Semicolons enforced; loop `break` / `continue` implemented |
| `[x]` | Core language | `foreach` over numeric bounds | Lowered to 0..N-1 loop |
| `[x]` | Core language | Function decls/calls, typed params/returns | CALL/RET |
| `[x]` | Core language | Void functions | `void` return type + implicit-void `function name(...)` supported |
| `[x]` | Core language | Return statements (implicit 0 if missing) | Implemented |
| `[x]` | Core language | Collections: literals + foreach over collections | Array literals + array foreach + typed array declarations/new(size) + `.length` + indexing + mutation + `append` / `removeAt` with preserved element typing |
| `[x]` | Core language | Optionals | `optional<T>` with `none`, `.hasValue`, `.value`, `.or(fallback)` |
| `[x]` | Core language | Enumerations | Implemented: `enum Name { Member; Other = 5; }`, strongly typed equality/assignment, and module export/import/re-export support |
| `[x]` | Core language | `switch` | Implemented: `switch value then { case expr then statement ... default then statement }`, no fallthrough, single evaluation of the switch value |
| `[x]` | Core language | Structs/records (user types) | Baseline `record` support implemented with copy-on-assignment/pass/return semantics |
| `[x]` | Core language | Integer base prefixes | `0b`, `0o`, and `0x` integer literals implemented |
| `[x]` | Type system | Decimal real literals and explicit casts | Implemented: `1.5`, `1.`, `.5`, numeric casts among `whole`/`integer`/`real`, enum-to-integer casts, and integer-to-enum casts with literal validation |
| `[x]` | Type system | Integer division semantics | Implemented: integral `/` truncates toward zero; use a `real` operand such as `1. / 2` or `1 as real / 2` for real division |
| `[x]` | Type system | Sized numeric boundary types | Implemented source-level types `integer8`, `integer16`, `integer32`, `whole8`, `whole16`, `whole32`, `real32`, `real64`; `byte` aliases `whole8`, `real64` aliases `real`, dynamic narrowing and sized stores are range-checked |
| `[~]` | Type system | Exact wide numerics, exponent real literals & literal suffixes (`i8/w8/r32` etc.) | `integer64` / `whole64`, exponent forms, and suffix literals remain deferred |
| `[~]` | Type system | Optional/`optional<T>` semantics | Baseline works; flow narrowing and stricter typing rules pending |
| `[_]` | Type system | Overload resolution rules (spec’d) | Engine not implemented |
| `[x]` | Error model | Runtime IP + call stack + snippets | Debug-map backed |
| `[x]` | Error model | Typed errors / exception objects in VM | VmError objects, THROW opcode, panic statement, tests |
| `[x]` | Error model | User-facing `fallible<Value, ErrorCode>` / `on error` syntax | Implemented: typed recoverable errors with enum/integer error codes, shorthand `fallible<Value>` as integer-coded fallible, `return error(code[, message])`, `return error(message)` for integer-coded fallibles, implicit handler `error.code` / `error.message`, and handler `yield` fallback |
| `[~]` | Error model | Fallible propagation shorthand | Deferred; v1 requires explicit `on error` handlers and does not include `try`/automatic propagation |
| `[x]` | Object model | Type references in AST/type checker (`TypeRef`) for named/generic user types | Implemented; parser/type-checker now use `TypeRef` instead of token-only types |
| `[x]` | Object model | Object symbol table pass (object names + fields + forward refs) | Implemented; duplicate checks + field type validation in place |
| `[x]` | Object model | Constructor symbol collection (typed signatures) | Implemented with signature-based overload resolution |
| `[x]` | Object model | VM heap object representation + opcodes (`NEW_OBJECT`, `GET_FIELD`, `SET_FIELD`) | Implemented with bytecode-v10 type layouts and numeric field slots |
| `[x]` | Object model | Parse + lower `new Type(...)`, `obj.field`, `obj.field = value` | Implemented for object construction + field read/write |
| `[x]` | Object model | Constructors + definite field initialization rules | Implemented: fields may use declaration defaults or constructor assignment; non-defaulted fields require definite constructor assignment |
| `[x]` | Object model | Field defaults | Implemented for object/record fields with `Type name = expression;`; defaults run before constructor bodies, and dependent initialization still belongs in constructors |
| `[x]` | Object model | Method declarations + call syntax (`obj.method(args)`) | Implemented with signature-based overload resolution; object methods support implicit-void authoring |
| `[x]` | Object model | Temporary method lowering (`method` -> static fn with implicit `this`) | Implemented with compile-time signature binding (no dynamic dispatch yet) |
| `[x]` | Object model | `record` declaration + value semantics (copy on assignment/pass/return) | Implemented: constructors, methods, inline/external interface implementations, copy-on-assignment/pass/return/container insertion, copy-by-value method receivers, structural equality for hashable records, and hashable record support in `map` / `set` |
| `[x]` | Object model | Top-level visibility/access control (`public/package/private`) | Implemented for module declarations; `public`/`package`/`private` gate imports, and legacy `export` remains as a compatibility alias for `public` |
| `[x]` | Object model | Member-level visibility/access control | Implemented for object/record fields, constructors, and methods; unmarked members default to `public`; `private` is type-local; `package` follows module package boundaries |
| `[x]` | Object model | Interfaces + conformance checks | Compile-time interface declarations + explicit `implement Interface for Object { ... via Object.method; }` mapping with signature/return checks, plus inline object-body syntax `implement Interface.method(...) { ... }` |
| `[~]` | Object model | Interface dispatch lowering/runtime model | Baseline runtime dispatch table opcode implemented for interface-typed locals/params/returns/fields/arrays; extend to broader container types and optimizer-grade fast paths |
| `[_]` | Object model | Lifecycle/perf plan for heap instances (GC/ownership strategy) | Permanent runtime design decision; defer until objects are exercised |
| `[~]` | Modules/imports | Exported declarations | Canonical `public` declarations plus legacy `export` compatibility are implemented for function/object/record/interface/enum declarations |
| `[~]` | Modules/imports | Import syntax & package declarations | `import Name [as Alias] from \"path\";` + `package Name;` implemented; package names now participate in `package` visibility checks, while broader package namespace semantics remain minimal |
| `[~]` | Modules/imports | Package search paths/stdlib layout/versioning | Relative-path + `lib/` ancestor search implemented; stdlib layout/versioning still deferred |
| `[x]` | Modules/imports | Package manifest parser/validator (`code.package.json`) | Implemented baseline schema v1 validation + target/capability checks + target override path validation |
| `[x]` | Modules/imports | Package lockfile + resolver (`code.lock.json`) | Implemented baseline local resolver (workspace `packages/` search), semver range checks (`x.y.z`, `^x.y.z`), deterministic lockfile emission with integrity hashes |
| `[x]` | Modules/imports | Library artifact format (`.codelib`) | Implemented baseline JSON container with embedded bytecode + package/target metadata; library manifests emit artifacts and lockfile prefers validated `.codelib` entries |
| `[x]` | Modules/imports | Package declarations | `package Name;` parsing + module-level validation (single declaration, ordered before imports/declarations) |
| `[x]` | Modules/imports | Module-scope symbol conflict checks | Detect duplicate top-level declarations and import-binding collisions within a module |
| `[x]` | Modules/imports | Import-chain diagnostics | Circular/missing-export/import resolution errors include module chain (`a -> b -> c`) |
| `[~]` | Modules/imports | Import ergonomics expansion | Alias support for object/interface/enum exports, grouped/selective import forms, namespace imports for function-only module surfaces, and re-export imports |
| `[x]` | Modules/imports | Module graph tooling | `--dump-module-graph [outputPath]` emits module graph (entry/modules/import edges) in text/json/dot; `--trace-linker` emits linker step trace |
| `[x]` | Bytecode/VM | Header v0x0A + debug table + `META` trailer | Implemented; v9 is rejected during alpha |
| `[x]` | Bytecode/VM | Core opcodes (arith/stack/jump/load/store/PRINT/CALL/RET/PUSH_STRING) | Implemented; includes object/array/optional/error primitives and `GET_TYPE_NAME` for interface dispatch lowering |
| `[~]` | Bytecode/VM | Constant pool for literals | String pool implemented in v10; additional literal kinds remain |
| `[x]` | Bytecode/VM | Recoverable fallible values | Implemented with VM-managed success/error variants and native/web opcode parity |
| `[x]` | Compiler pipeline | Lexer/parser/AST | Implemented |
| `[x]` | Compiler pipeline | Codegen to VM bytecode | Implemented |
| `[x]` | Compiler pipeline | CLI: compile/run/disasm/token-dump | Implemented |
| `[!]` | Compiler pipeline | Type checker tightening (def-assignment done; improve return/flow) | Continue refining |
| `[~]` | Compiler pipeline | Frame sizing/temp management | Function-local slots are unique; CALL uses precise frame size; temps reused within foreach; further temp reuse/liveness possible |
| `[~]` | Compiler pipeline | Optimizations: const fold/DCE | Initial literal fold in place |
| `[~]` | Runtime/stdlib | Basic stdlib (IO/math/time) | Print + time intrinsics (`unixMilliseconds`, `unixMicroseconds`, `monotonicNanoseconds`, `monotonicTicks`, `monotonicTicksPerSecond`) and native-only `readLine` + `sleepMilliseconds` are implemented; expand broader stdlib surface |
| `[x]` | Runtime/stdlib | Math helpers, constants, and randomness | Implemented on `vm-native` and `vm-web`: `minimum`, `maximum`, `absolute`, `sign`, `lerp`, `sine`, `cosine`, `squareRoot`, `random`, plus built-in constants `pi` and `tau` |
| `[x]` | Runtime/stdlib | Collections beyond arrays | Implemented: `map`, `set`, `queue`, and `stack` with shared `.length`; `map` indexing/get-set/contains/remove; `set` add/contains/remove; `queue` enqueue/dequeue/peek; `stack` push/pop/peek |
| `[x]` | Platform/targets | Compile target model (`--target vm-native|vm-web`) | Implemented: target threads through linker/codegen entry points; hidden maintainer target flag remains available |
| `[x]` | Platform/targets | Target capability validation | Implemented baseline: inferred capability groups (`std.*`, `engine.*`) from package/imports with compile-time matrix checks (`vm-web` rejects `std.fs`) |
| `[x]` | Platform/targets | Web app/runtime V1 contract | Documented in `docs/web-app-v1.md`: scene object convention, `start/update/draw` plus optional `drawHud`, full-window browser runtime, centered `640x360` safe area, hybrid-expanded framing, current primitive/image-sprite/keyboard/primary-pointer/diagnostics scope, wrapper-layer guidance, and static site folder target |
| `[~]` | Platform/targets | Host ABI baseline | Implemented baseline `HOST_CALL` opcode + host binding tables for native/web modes (`standard.input_output.print`, `std.time.*`, native-only source calls `readLine()` / `sleepMilliseconds()` with diagnostics) and engine stubs (`engine.window/input/gfx`); compile-time capability inference includes host-lowered intrinsics |
| `[x]` | Platform/targets | Static-site web build workflow | Implemented: public `.code` input emits `index.html`, `code-runtime.wasm`, embedded bytecode/direct-file fallback, copied assets, and optional `app.bytecode` via `--emit-web-bytecode` |
| `[x]` | Platform/targets | Web build artifact polish | Implemented notes-derived change: bytecode is embedded in `index.html` by default for direct opening, and `app.bytecode` is emitted only with `--emit-web-bytecode` |
| `[~]` | Platform/targets | Browser-backed web app runtime | Implemented current slice: a dedicated worker hosts the Rust/Wasm bytecode-v10 VM, fixed/continuous update scheduling, lifecycle, profiling, and draw encoding; the main thread owns Canvas, input, audio, visibility, and `requestAnimationFrame`; direct `file://` uses the embedded Wasm fallback |
| `[~]` | Platform/targets | Graphical app profile | Implemented first web-entry slice: top-level `start` / `update` / `draw` / optional `drawHud`, same-module global app state, usage-based implied engine imports across web-app modules (`Draw` / `Input` / `Viewport` / `Colors` / `Diagnostics` / `Runtime` / `Audio` plus direct `Color` and canonical `engine.scene` types), synthesized `MainScene`, and explicit `MainScene` compatibility; broader target-agnostic reuse remains pending |
| `[~]` | Platform/targets | Web engine host bindings | Implemented current scene-runtime bindings for `inputKeyDown`, `inputPointerWorldX`, `inputPointerWorldY`, `inputPointerScreenX`, `inputPointerScreenY`, `inputPointerIsDown`, `inputPointerWasPressed`, `inputPointerWasReleased`, `clear`, `drawRectangle`, `drawRectangleOutline`, `drawLine`, `drawCircle`, `drawCircleOutline`, `drawPolygon`, `drawPolygonOutline`, `drawText`, `drawImage`, `drawSprite`, `cameraView*`, `cameraSafe*`, `screenWidth` / `screenHeight`, `diagnosticsLast*`, and `audio*`; legacy window/input/gfx handle-based stubs still need real browser-backed behavior or wrappers |
| `[!]` | Platform/targets | Backend-agnostic engine API contract | Lock capability-query + fallback semantics so Code source remains portable across web/native backends |
| `[~]` | Platform/targets | Native/web parity validation | Single-source app/game runs across both targets with explicit capability fallback behavior |
| `[~]` | Game engine | Engine core packages | Canonical `engine.scene` now exports `Scene`, `SceneLoop`, and lifecycle interfaces for explicit child-object composition; `engine.math` / broader engine packages still pending |
| `[~]` | Game engine | Engine platform adapters | Wrapper layer now includes canonical `engine.colors`, `engine.drawing`, `engine.input`, `engine.viewport`, `engine.diagnostics`, `engine.runtime`, `engine.audio`, and `engine.scene` with compatibility `engine.view` / `engine.loop`; still need fuller host-backed packages, a future byte-channel `rgba(byte, byte, byte, byte)` helper on top of the implemented `byte` / `whole8` surface, and fuller audio mixer controls |
| `[~]` | Game engine | `engine.gpu` ABI v1 | GPU resource/pipeline/dispatch API for simulation/ML/graphics workloads |
| `[~]` | Game engine | WebGPU backend | Implement `engine.gpu` via WebGPU on `vm-web` with fallback policy |
| `[~]` | Game engine | Native GPU backend parity | Implement same `engine.gpu` ABI on `vm-native` backend(s) |
| `[_]` | Runtime/stdlib | REPL | Future |
| `[x]` | Tooling/docs | Bytecode spec v0.8 (debug map + arrays/optionals/errors/objects) | Implemented |
| `[x]` | Tooling/docs | AI context + sample programs | Implemented |
| `[~]` | Tooling/docs | Disassembler/trace polish; CLI trace flags | Linker trace flags implemented; disassembler/trace polish remains |
| `[~]` | Tooling/docs | Formatter/linter for `.code` | Medium priority |
| `[x]` | Testing | VM harness tests | Implemented; includes host ABI conformance checks (native-only diagnostics, engine stubs, target parity) |
| `[x]` | Testing | Compiler integration suite | Includes print, arithmetic, functions, loops, foreach, strings |
| `[x]` | Testing | Property/fuzz tests (parser/VM) | Arithmetic, boolean logic, string concat, loop sums |
| `[!]` | Testing | Object-model integration suite | Constructor/new/field/method paths + compile-time/runtime interface diagnostics (including interface-typed fields/arrays, record copy semantics, and scene composition registries) covered; expand broader containers and record follow-up behavior |
| `[~]` | Testing | Object-model fuzz/property domains | Constructor/field mutation/member access invariants |

## Priority Rollup (benefit/effort)
- High (`[!]`): keeping example/docs status aligned with implementation truth and tightening the broader target-agnostic direction of the graphical app profile.
- Medium (`[~]`): fallible propagation shorthand, exact wide numerics/suffixes/exponent literals, advanced browser input/content handling, fuller audio mixing, constant pool, optimizer expansion, tooling polish, and engine core/adapters.
- Low (`[_]`): REPL, future stdlib/versioning, long-term runtime lifecycle strategy, remote package registry, and longer-horizon GPU/backend work.

See also: `docs/platform-roadmap.md` for ABI schema, package manifest schema, and phased execution plan.
