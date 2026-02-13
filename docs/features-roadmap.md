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
| `[x]` | Core language | Arithmetic and comparisons | Implemented |
| `[x]` | Core language | Logical `and`/`or`/`not` (short-circuit) | Implemented |
| `[x]` | Core language | Boolean literals `true` / `false` | Implemented |
| `[x]` | Core language | String literals + interpolation; `+` concat | Implemented |
| `[x]` | Core language | Control flow: `if/while/for` with mandatory `then` | Semicolons enforced |
| `[x]` | Core language | `foreach` over numeric bounds | Lowered to 0..N-1 loop |
| `[x]` | Core language | Function decls/calls, typed params/returns | CALL/RET |
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
| `[~]` | Object model | `record` value semantics (copy on assignment/pass/return) | Permanent feature, follows object core |
| `[~]` | Object model | Visibility enforcement (`public/package/private`) | Permanent; phase in from parser -> checker -> codegen |
| `[x]` | Object model | Interfaces + `implement ... for ...` conformance checks | Compile-time interface declarations + explicit `implement Interface for Object { ... via Object.method; }` mapping with signature/return checks |
| `[~]` | Object model | Interface dispatch lowering/runtime model | Baseline runtime dispatch table opcode implemented for interface-typed locals/params/returns/fields; extend to interface collections and optimizer-grade fast paths |
| `[_]` | Object model | Lifecycle/perf plan for heap instances (GC/ownership strategy) | Permanent runtime design decision; defer until objects are exercised |
| `[~]` | Modules/imports | Exported declarations | `export` implemented for function/object/interface declarations |
| `[~]` | Modules/imports | Import syntax & package declarations | `import Name [as Alias] from \"path\";` + `package Name;` implemented; package namespace semantics still minimal |
| `[~]` | Modules/imports | Package search paths/stdlib layout/versioning | Relative-path + `lib/` ancestor search implemented; stdlib layout/versioning still deferred |
| `[x]` | Modules/imports | Package declarations | `package Name;` parsing + module-level validation (single declaration, ordered before imports/declarations) |
| `[x]` | Modules/imports | Module-scope symbol conflict checks | Detect duplicate top-level declarations and import-binding collisions within a module |
| `[x]` | Modules/imports | Import-chain diagnostics | Circular/missing-export/import resolution errors include module chain (`a -> b -> c`) |
| `[~]` | Modules/imports | Import ergonomics expansion | Alias support for object/interface exports and grouped/selective import forms |
| `[~]` | Modules/imports | Module graph tooling | Planned `--dump-module-graph` and richer linker tracing |
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
| `[~]` | Runtime/stdlib | Basic stdlib (IO/math/time) | Print exists; extend |
| `[_]` | Runtime/stdlib | REPL | Future |
| `[x]` | Tooling/docs | Bytecode spec v0.8 (debug map + arrays/optionals/errors/objects) | Implemented |
| `[x]` | Tooling/docs | AI context + sample programs | Implemented |
| `[~]` | Tooling/docs | Disassembler/trace polish; CLI trace flags | Medium priority |
| `[~]` | Tooling/docs | Formatter/linter for `.code` | Medium priority |
| `[x]` | Testing | VM harness tests | Implemented |
| `[x]` | Testing | Compiler integration suite | Includes print, arithmetic, functions, loops, foreach, strings |
| `[x]` | Testing | Property/fuzz tests (parser/VM) | Arithmetic, boolean logic, string concat, loop sums |
| `[!]` | Testing | Object-model integration suite | Constructor/new/field/method paths + compile-time/runtime interface diagnostics (including interface-typed fields) covered; expand for records and interface collections |
| `[~]` | Testing | Object-model fuzz/property domains | Constructor/field mutation/member access invariants |

## Priority Rollup (benefit/effort)
- High (`[!]`): interface/dynamic dispatch model, type system/codegen polish (frame sizing, flow/return analysis), testing expansion, error propagation semantics.
- Medium (`[~]`): record semantics, constant pool, optimizer expansion, stdlib basics, tooling polish.
- Low (`[_]`): interfaces runtime dispatch, modules/packages, REPL, future stdlib/versioning, long-term runtime lifecycle strategy.
