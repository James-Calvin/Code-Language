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
| `[x]` | Core language | Collections: literals + foreach over collections | Array literals + array foreach + typed array declarations/new(size) + `.length` |
| `[_]` | Core language | Structs/records (user types) | Beyond current object model |
| `[~]` | Type system | Sized numerics & literal suffixes (`i8/w8/r32` etc.) | Lexer/parser/type support pending |
| `[~]` | Type system | Optional/`optional<T>` semantics | Nullable/absence model not implemented |
| `[_]` | Type system | Overload resolution rules (spec’d) | Engine not implemented |
| `[x]` | Error model | Runtime IP + call stack + snippets | Debug-map backed |
| `[x]` | Error model | Typed errors / exception objects in VM | VmError objects, THROW opcode, panic statement, tests |
| `[!]` | Error model | Propagation semantics across functions | Requires compiler/codegen rules |
| `[_]` | Object model | Interfaces/objects/records with visibility (`public/package/private`) | Parsing/codegen/runtime not implemented |
| `[_]` | Modules/imports | Exported declarations | Export syntax in spec; not implemented |
| `[_]` | Modules/imports | Import syntax & package declarations | Not implemented |
| `[_]` | Modules/imports | Package search paths/stdlib layout/versioning | Deferred until core stabilizes |
| `[x]` | Bytecode/VM | Header v0x02 + debug table | Implemented |
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
| `[x]` | Tooling/docs | Bytecode spec v0.4 (debug map) | Implemented |
| `[x]` | Tooling/docs | AI context + sample programs | Implemented |
| `[~]` | Tooling/docs | Disassembler/trace polish; CLI trace flags | Medium priority |
| `[~]` | Tooling/docs | Formatter/linter for `.code` | Medium priority |
| `[x]` | Testing | VM harness tests | Implemented |
| `[x]` | Testing | Compiler integration suite | Includes print, arithmetic, functions, loops, foreach, strings |
| `[x]` | Testing | Property/fuzz tests (parser/VM) | Arithmetic, boolean logic, string concat, loop sums |

## Priority Rollup (benefit/effort)
- High (`[!]`): type system/codegen polish (frame sizing, flow/return analysis), testing (integration + fuzz), typed errors/propagation, VM exceptions, formatter/trace? (see table), compiler integration tests, fuzz tests.
- Medium (`[~]`): collections, constant pool, optimizer expansion, stdlib basics, tooling polish, formatter/linter.
- Low (`[_]`): modules/packages, records/structs, REPL, future stdlib/versioning.
