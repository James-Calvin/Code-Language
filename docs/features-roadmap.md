# Code Language Features Roadmap

Legend:  
- `[x]` complete  
- `[!]` high priority  
- `[~]` medium priority  
- `[_]` low priority

## Status & Priority Table

|  Priority | Area | Item | Notes |
| --- | --- | --- | --- |
| `[x]` | Core language | Required type annotations; primitive numerics/string/boolean | Implemented |
| `[x]` | Core language | Variables, assignments, blocks | Implemented |
| `[x]` | Core language | Constants (`constant` declarations) | `constant Type name = value;` implemented with reassignment errors |
| `[x]` | Core language | Enhanced assignment operators | `+=`, `-=`, `*=`, `/=`, `%=` and unary `++`/`--` implemented for variable targets |
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
| `[x]` | Core language | Collections: literals + foreach over collections | Array literals + array foreach + typed array declarations/new(size) + `.length` + indexing + mutation |
| `[x]` | Core language | Optionals | `optional<T>` with `none`, `.hasValue`, `.value`, `.or(fallback)` |
| `[~]` | Core language | Structs/records (user types) | Planned via object-model build-out below |
| `[~]` | Type system | Sized numerics & literal suffixes (`i8/w8/r32` etc.) | Lexer/parser/type support pending |
| `[~]` | Type system | Optional/`optional<T>` semantics | Baseline works; flow narrowing and stricter typing rules pending |
| `[_]` | Type system | Overload resolution rules (spec’d) | Engine not implemented |
| `[x]` | Error model | Runtime IP + call stack + snippets | Debug-map backed |
| `[x]` | Error model | Typed errors / exception objects in VM | VmError objects, THROW opcode, panic statement, tests |
| `[!]` | Error model | Propagation semantics across functions | Requires compiler/codegen rules |
| `[x]` | Object model | Type references in AST/type checker (`TypeRef`) for named/generic user types | Implemented; parser/type-checker now use `TypeRef` instead of token-only types |
| `[x]` | Object model | Object symbol table pass (object names + fields + forward refs) | Implemented; duplicate checks + field type validation in place |
| `[x]` | Object model | Constructor symbol collection (typed signatures) | Implemented with signature-based overload resolution |
| `[x]` | Object model | VM heap object representation + opcodes (`NEW_OBJECT`, `GET_FIELD`, `SET_FIELD`) | Implemented with runtime object field dictionary |
| `[x]` | Object model | Parse + lower `new Type(...)`, `obj.field`, `obj.field = value` | Implemented for object construction + field read/write |
| `[x]` | Object model | Constructors + definite field initialization rules | Implemented baseline: fields require constructor init; constructors enforced |
| `[x]` | Object model | Method declarations + call syntax (`obj.method(args)`) | Implemented with signature-based overload resolution |
| `[x]` | Object model | Temporary method lowering (`method` -> static fn with implicit `this`) | Implemented with compile-time signature binding (no dynamic dispatch yet) |
| `[~]` | Object model | `record` declaration + value semantics (copy on assignment/pass/return) | Add `record` parser/type model first, then non-reference copy semantics |
| `[~]` | Object model | Visibility enforcement (`public/package/private`) | Permanent; phase in from parser -> checker -> codegen |
| `[x]` | Object model | Interfaces + `implement ... for ...` conformance checks | Compile-time interface declarations + explicit `implement Interface for Object { ... via Object.method; }` mapping with signature/return checks |
| `[~]` | Object model | Interface dispatch lowering/runtime model | Baseline runtime dispatch table opcode implemented for interface-typed locals/params/returns/fields; extend to interface collections and optimizer-grade fast paths |
| `[_]` | Object model | Lifecycle/perf plan for heap instances (GC/ownership strategy) | Permanent runtime design decision; defer until objects are exercised |
| `[~]` | Modules/imports | Exported declarations | `export` implemented for function/object/interface declarations |
| `[~]` | Modules/imports | Import syntax & package declarations | `import Name [as Alias] from \"path\";` + `package Name;` implemented; package namespace semantics still minimal |
| `[~]` | Modules/imports | Package search paths/stdlib layout/versioning | Relative-path + `lib/` ancestor search implemented; stdlib layout/versioning still deferred |
| `[x]` | Modules/imports | Package manifest parser/validator (`code.package.json`) | Implemented baseline schema v1 validation + target/capability checks + target override path validation |
| `[x]` | Modules/imports | Package lockfile + resolver (`code.lock.json`) | Implemented baseline local resolver (workspace `packages/` search), semver range checks (`x.y.z`, `^x.y.z`), deterministic lockfile emission with integrity hashes |
| `[x]` | Modules/imports | Library artifact format (`.codelib`) | Implemented baseline JSON container with embedded bytecode + package/target metadata; library manifests emit artifacts and lockfile prefers validated `.codelib` entries |
| `[x]` | Modules/imports | Package declarations | `package Name;` parsing + module-level validation (single declaration, ordered before imports/declarations) |
| `[x]` | Modules/imports | Module-scope symbol conflict checks | Detect duplicate top-level declarations and import-binding collisions within a module |
| `[x]` | Modules/imports | Import-chain diagnostics | Circular/missing-export/import resolution errors include module chain (`a -> b -> c`) |
| `[~]` | Modules/imports | Import ergonomics expansion | Alias support for object/interface exports and grouped/selective import forms |
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
| `[~]` | Runtime/stdlib | Basic stdlib (IO/math/time) | Print + time intrinsics (`unix_ms`, `unix_us`, `mono_ns`, `mono_ticks`, `mono_ticks_per_second`) implemented; native-only `read_line` + `sleep_ms` added with target checks; expand broader IO/math surface |
| `[x]` | Platform/targets | Compile target model (`--target vm-native|vm-web`) | Implemented: target threads through linker/codegen entry points; default `vm-native` |
| `[x]` | Platform/targets | Target capability validation | Implemented baseline: inferred capability groups (`std.*`, `engine.*`) from package/imports with compile-time matrix checks (`vm-web` rejects `std.fs`) |
| `[~]` | Platform/targets | Host ABI baseline | Implemented baseline `HOST_CALL` opcode + host binding tables for native/web modes (`std.io.print`, `std.time.*`, native-only `std.io.read_line`/`std.time.sleep_ms` with diagnostics) and engine stubs (`engine.window/input/gfx`); compile-time capability inference includes host-lowered intrinsics |
| `[!]` | Platform/targets | Web VM target | Browser runtime preview implemented (`web-runtime/` JS bytecode harness + web host binding table for `std.io.print`/`std.time.*`); continue toward production loader/packaging |
| `[~]` | Platform/targets | Native/web parity validation | Single-source app/game runs across both targets |
| `[~]` | Game engine | Engine core packages | `engine.math`, `engine.ecs`, `engine.scene`, `engine.loop` |
| `[~]` | Game engine | Engine platform adapters | Window/input/gfx/audio host-backed packages |
| `[_]` | Runtime/stdlib | REPL | Future |
| `[x]` | Tooling/docs | Bytecode spec v0.8 (debug map + arrays/optionals/errors/objects) | Implemented |
| `[x]` | Tooling/docs | AI context + sample programs | Implemented |
| `[~]` | Tooling/docs | Disassembler/trace polish; CLI trace flags | Linker trace flags implemented; disassembler/trace polish remains |
| `[~]` | Tooling/docs | Formatter/linter for `.code` | Medium priority |
| `[x]` | Testing | VM harness tests | Implemented; includes host ABI conformance checks (native-only diagnostics, engine stubs, target parity) |
| `[x]` | Testing | Compiler integration suite | Includes print, arithmetic, functions, loops, foreach, strings |
| `[x]` | Testing | Property/fuzz tests (parser/VM) | Arithmetic, boolean logic, string concat, loop sums |
| `[!]` | Testing | Object-model integration suite | Constructor/new/field/method paths + compile-time/runtime interface diagnostics (including interface-typed fields) covered; expand for records and interface collections |
| `[~]` | Testing | Object-model fuzz/property domains | Constructor/field mutation/member access invariants |

## Priority Rollup (benefit/effort)
- High (`[!]`): interface/dynamic dispatch model, type system/codegen polish (frame sizing, flow/return analysis), testing expansion, error propagation semantics, host ABI + web VM target.
- Medium (`[~]`): record semantics, constant pool, optimizer expansion, stdlib basics, tooling polish, engine core/adapters and cross-target parity.
- Low (`[_]`): interfaces runtime dispatch, REPL, future stdlib/versioning, long-term runtime lifecycle strategy, remote package registry.

See also: `docs/platform-roadmap.md` for ABI schema, package manifest schema, and phased execution plan.
