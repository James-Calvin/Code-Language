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
| `[!]` | Object model | Object symbol table pass (objects/records, fields, constructors, methods) | Permanent prerequisite; enables duplicate checks + forward refs |
| `[!]` | Object model | VM heap object representation + opcodes (`NEW_OBJECT`, `GET_FIELD`, `SET_FIELD`) | Permanent runtime substrate for user-defined instances |
| `[!]` | Object model | Parse + lower `new Type(...)`, `obj.field`, `obj.field = value` | Permanent surface syntax; unblocks first useful object programs |
| `[!]` | Object model | Constructors + definite field initialization rules | Permanent safety requirement for object creation |
| `[~]` | Object model | Method declarations + call syntax (`obj.method(args)`) | Permanent syntax; initial lowering can be simplified |
| `[~]` | Object model | Temporary method lowering (`method` -> static fn with implicit `this`) | Temporary bridge to ship methods before dynamic dispatch |
| `[~]` | Object model | `record` value semantics (copy on assignment/pass/return) | Permanent feature, follows object core |
| `[~]` | Object model | Visibility enforcement (`public/package/private`) | Permanent; phase in from parser -> checker -> codegen |
| `[_]` | Object model | Interfaces + `implement ... for ...` conformance checks | Permanent; depends on methods + type refs |
| `[_]` | Object model | Interface dispatch lowering/runtime model | Permanent; likely vtable/itable or thunk-based |
| `[_]` | Object model | Lifecycle/perf plan for heap instances (GC/ownership strategy) | Permanent runtime design decision; defer until objects are exercised |
| `[_]` | Modules/imports | Exported declarations | Export syntax in spec; not implemented |
| `[_]` | Modules/imports | Import syntax & package declarations | Not implemented |
| `[_]` | Modules/imports | Package search paths/stdlib layout/versioning | Deferred until core stabilizes |
| `[x]` | Bytecode/VM | Header v0x04 + debug table | Implemented |
| `[x]` | Bytecode/VM | Core opcodes (arith/stack/jump/load/store/PRINT/CALL/RET/PUSH_STRING) | Implemented |
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
| `[x]` | Tooling/docs | Bytecode spec v0.7 (debug map + arrays/optionals/errors) | Implemented |
| `[x]` | Tooling/docs | AI context + sample programs | Implemented |
| `[~]` | Tooling/docs | Disassembler/trace polish; CLI trace flags | Medium priority |
| `[~]` | Tooling/docs | Formatter/linter for `.code` | Medium priority |
| `[x]` | Testing | VM harness tests | Implemented |
| `[x]` | Testing | Compiler integration suite | Includes print, arithmetic, functions, loops, foreach, strings |
| `[x]` | Testing | Property/fuzz tests (parser/VM) | Arithmetic, boolean logic, string concat, loop sums |
| `[!]` | Testing | Object-model integration suite | Gate for object milestones (construct/field/method/record/interface) |
| `[~]` | Testing | Object-model fuzz/property domains | Constructor/field mutation/member access invariants |

## Priority Rollup (benefit/effort)
- High (`[!]`): object prerequisites (type refs, symbols, object ops, constructors), type system/codegen polish (frame sizing, flow/return analysis), testing expansion, error propagation semantics.
- Medium (`[~]`): methods (via temporary lowering), record semantics, constant pool, optimizer expansion, stdlib basics, tooling polish.
- Low (`[_]`): interfaces runtime dispatch, modules/packages, REPL, future stdlib/versioning, long-term runtime lifecycle strategy.
