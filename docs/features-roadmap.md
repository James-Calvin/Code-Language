# Code Language Features Roadmap

Legend:  
- `[x]` complete  
- `[!]` high priority  
- `[~]` medium priority  
- `[_]` low priority

This roadmap is implementation-truthful: items marked below are gaps from the current compiler/runtime surface, not draft syntax that already exists.

## Near-Term Gap Groups

### Language Gaps
1. User-facing error-handling syntax and propagation (`fallible<T>`, `on error`)
2. Member-level visibility/access control follow-up

### Stdlib and Runtime Gaps
1. Broader standard-library modules after the core container/math baseline lands

### Engine Gaps
1. Broader engine wrapper packages
2. Richer browser input/audio/content handling
3. Longer-term GPU/backend work

## Status & Priority Table

|  Priority | Area | Item | Notes |
| --- | --- | --- | --- |
| `[x]` | Core language | Required type annotations; primitive numerics/string/boolean | Implemented |
| `[x]` | Core language | Variables, assignments, blocks | Implemented |
| `[x]` | Core language | Constants (`constant` declarations) | `constant Type name = value;` implemented with reassignment errors |
| `[x]` | Core language | Enhanced assignment operators | `+=`, `-=`, `*=`, `/=`, `%=` and unary `++`/`--` implemented for variables, object fields, array elements, and map entries |
| `[x]` | Core language | Arithmetic and comparisons | Implemented |
| `[x]` | Core language | Modulo operator (`%`) | Implemented in lexer/parser/type-checker/codegen/VM |
| `[x]` | Core language | Logical `and`/`or`/`not` (short-circuit) | Implemented |
| `[x]` | Core language | Boolean literals `true` / `false` | Implemented |
| `[x]` | Core language | String literals + interpolation; `+` concat | Implemented |
| `[x]` | Core language | Interpolation expression parity | Interpolation now parses full expressions (member/index/call/ops) inside `{...}` |
| `[x]` | Core language | Control flow: `if/while/for` with mandatory `then` | Semicolons enforced |
| `[x]` | Core language | `foreach` over numeric bounds | Lowered to 0..N-1 loop |
| `[x]` | Core language | Function decls/calls, typed params/returns | CALL/RET |
| `[x]` | Core language | Void functions | `void` return type + implicit-void `function name(...)` supported |
| `[x]` | Core language | Return statements (implicit 0 if missing) | Implemented |
| `[x]` | Core language | Collections: literals + foreach over collections | Array literals + array foreach + typed array declarations/new(size) + `.length` + indexing + mutation + `append` / `remove_at` with preserved element typing |
| `[x]` | Core language | Optionals | `optional<T>` with `none`, `.hasValue`, `.value`, `.or(fallback)` |
| `[x]` | Core language | Enumerations | Implemented: `enum Name { Member; Other = 5; }`, strongly typed equality/assignment, and module export/import/re-export support |
| `[x]` | Core language | `switch` | Implemented: `switch value then { case expr then statement ... default then statement }`, no fallthrough, single evaluation of the switch value |
| `[x]` | Core language | Structs/records (user types) | Baseline `record` support implemented with copy-on-assignment/pass/return semantics |
| `[~]` | Type system | Sized numerics & literal suffixes (`i8/w8/r32` etc.) | Lexer/parser/type support pending |
| `[~]` | Type system | Optional/`optional<T>` semantics | Baseline works; flow narrowing and stricter typing rules pending |
| `[_]` | Type system | Overload resolution rules (spec’d) | Engine not implemented |
| `[x]` | Error model | Runtime IP + call stack + snippets | Debug-map backed |
| `[x]` | Error model | Typed errors / exception objects in VM | VmError objects, THROW opcode, panic statement, tests |
| `[~]` | Error model | User-facing `fallible<T>` / `on error` syntax and propagation | Spec draft exists, but parser/type-checker/codegen do not implement it today |
| `[x]` | Object model | Type references in AST/type checker (`TypeRef`) for named/generic user types | Implemented; parser/type-checker now use `TypeRef` instead of token-only types |
| `[x]` | Object model | Object symbol table pass (object names + fields + forward refs) | Implemented; duplicate checks + field type validation in place |
| `[x]` | Object model | Constructor symbol collection (typed signatures) | Implemented with signature-based overload resolution |
| `[x]` | Object model | VM heap object representation + opcodes (`NEW_OBJECT`, `GET_FIELD`, `SET_FIELD`) | Implemented with runtime object field dictionary |
| `[x]` | Object model | Parse + lower `new Type(...)`, `obj.field`, `obj.field = value` | Implemented for object construction + field read/write |
| `[x]` | Object model | Constructors + definite field initialization rules | Implemented baseline: fields require constructor init; constructors enforced |
| `[x]` | Object model | Method declarations + call syntax (`obj.method(args)`) | Implemented with signature-based overload resolution; object methods support implicit-void authoring |
| `[x]` | Object model | Temporary method lowering (`method` -> static fn with implicit `this`) | Implemented with compile-time signature binding (no dynamic dispatch yet) |
| `[x]` | Object model | `record` declaration + value semantics (copy on assignment/pass/return) | Implemented: constructors, methods, inline/external interface implementations, copy-on-assignment/pass/return/container insertion, copy-by-value method receivers, structural equality for hashable records, and hashable record support in `map` / `set` |
| `[x]` | Object model | Top-level visibility/access control (`public/package/private`) | Implemented for module declarations; `public`/`package`/`private` gate imports, and legacy `export` remains as a compatibility alias for `public` |
| `[~]` | Object model | Member-level visibility/access control | Fields and methods still use the current all-visible member model |
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
| `[x]` | Bytecode/VM | Header v0x05 + debug table | Implemented |
| `[x]` | Bytecode/VM | Core opcodes (arith/stack/jump/load/store/PRINT/CALL/RET/PUSH_STRING) | Implemented; includes object/array/optional/error primitives and `GET_TYPE_NAME` for interface dispatch lowering |
| `[~]` | Bytecode/VM | Constant pool for literals | To reduce code size |
| `[!]` | Bytecode/VM | Real exception/error objects | Needed for typed errors |
| `[x]` | Compiler pipeline | Lexer/parser/AST | Implemented |
| `[x]` | Compiler pipeline | Codegen to VM bytecode | Implemented |
| `[x]` | Compiler pipeline | CLI: compile/run/disasm/token-dump | Implemented |
| `[!]` | Compiler pipeline | Type checker tightening (def-assignment done; improve return/flow) | Continue refining |
| `[~]` | Compiler pipeline | Frame sizing/temp management | Function-local slots are unique; CALL uses precise frame size; temps reused within foreach; further temp reuse/liveness possible |
| `[~]` | Compiler pipeline | Optimizations: const fold/DCE | Initial literal fold in place |
| `[~]` | Runtime/stdlib | Basic stdlib (IO/math/time) | Print + time intrinsics (`unix_ms`, `unix_us`, `mono_ns`, `mono_ticks`, `mono_ticks_per_second`) and native-only `read_line` + `sleep_ms` are implemented; expand broader stdlib surface |
| `[x]` | Runtime/stdlib | Math helpers and randomness | Implemented on `vm-native` and `vm-web`: `minimum`, `maximum`, `absolute`, `sign`, `lerp`, `sine`, `cosine`, `random` |
| `[x]` | Runtime/stdlib | Collections beyond arrays | Implemented: `map`, `set`, `queue`, and `stack` with shared `.length`; `map` indexing/get-set/contains/remove; `set` add/contains/remove; `queue` enqueue/dequeue/peek; `stack` push/pop/peek |
| `[x]` | Platform/targets | Compile target model (`--target vm-native|vm-web`) | Implemented: target threads through linker/codegen entry points; default `vm-native` |
| `[x]` | Platform/targets | Target capability validation | Implemented baseline: inferred capability groups (`std.*`, `engine.*`) from package/imports with compile-time matrix checks (`vm-web` rejects `std.fs`) |
| `[x]` | Platform/targets | Web app/runtime V1 contract | Documented in `docs/web-app-v1.md`: scene object convention, `start/update/draw` plus optional `draw_hud`, full-window browser runtime, centered `640x360` safe area, hybrid-expanded framing, current primitive/image-sprite/keyboard scope, wrapper-layer guidance, and static site folder target |
| `[~]` | Platform/targets | Host ABI baseline | Implemented baseline `HOST_CALL` opcode + host binding tables for native/web modes (`standard.input_output.print`, `std.time.*`, native-only `standard.input_output.read_line`/`std.time.sleep_ms` with diagnostics) and engine stubs (`engine.window/input/gfx`); compile-time capability inference includes host-lowered intrinsics |
| `[x]` | Platform/targets | Static-site web build workflow | Implemented first slice: `--build-web` emits a runnable `dist/` folder (or custom `--out`) with `index.html` + `app.bytecode` |
| `[~]` | Platform/targets | Browser-backed web app runtime | Implemented current slice: generated full-window canvas runtime owns resize, timing, `MainScene` lifecycle, optional `draw_hud`, centered `640x360` safe area, hybrid-expanded framing, copied `assets/` output, and rectangles/outlines/lines/circles/polygons/text/images/sprites; expand richer input/audio/content handling |
| `[~]` | Platform/targets | Web engine host bindings | Implemented current scene-runtime bindings for `key_down`, `clear`, `draw_rectangle`, `draw_rectangle_outline`, `draw_line`, `draw_circle`, `draw_circle_outline`, `draw_polygon`, `draw_polygon_outline`, `draw_text`, `draw_image`, `draw_sprite`, `camera_view_*`, `camera_safe_*`, and `screen_width` / `screen_height`; legacy window/input/gfx handle-based stubs still need real browser-backed behavior or wrappers |
| `[!]` | Platform/targets | Backend-agnostic engine API contract | Lock capability-query + fallback semantics so Code source remains portable across web/native backends |
| `[~]` | Platform/targets | Native/web parity validation | Single-source app/game runs across both targets with explicit capability fallback behavior |
| `[~]` | Game engine | Engine core packages | Canonical `engine.scene` now exports `Scene`, `SceneLoop`, and lifecycle interfaces for explicit child-object composition; `engine.math` / broader engine packages still pending |
| `[~]` | Game engine | Engine platform adapters | Wrapper layer now includes canonical `engine.colors`, `engine.drawing`, `engine.input`, `engine.viewport`, and `engine.scene` with compatibility `engine.view` / `engine.loop`; still need fuller host-backed packages including audio |
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
- High (`[!]`): keeping example/docs status aligned with implementation truth.
- Medium (`[~]`): member-level visibility/access control, fuller user-facing error-handling syntax, constant pool, optimizer expansion, tooling polish, and engine core/adapters.
- Low (`[_]`): REPL, future stdlib/versioning, long-term runtime lifecycle strategy, remote package registry, and longer-horizon GPU/backend work.

See also: `docs/platform-roadmap.md` for ABI schema, package manifest schema, and phased execution plan.

