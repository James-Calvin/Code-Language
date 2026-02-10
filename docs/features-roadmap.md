# Code Language Features Roadmap

Legend: [x] implemented (prototype), [~] planned/next, [ ] deferred/open

## Core language
- [x] Required type annotations; primitive numerics/string/boolean
- [x] Variables, assignments, blocks
- [x] Arithmetic and comparisons
- [x] Logical `and` / `or` / `not` (short-circuit)
- [x] Boolean literals `true` / `false`
 - [x] String literals with interpolation (expressions inside `{}`) and concat via `+`
- [x] Control flow: `if ... then ... else`, `while ... then ...`, `for` (counted)
- [x] `foreach` lowered to a 0..N-1 loop over an integer bound
- [x] Function declarations and calls (CALL/RET), untyped parameters
- [x] Return statements (implicit 0 if missing)
- [x] Typed functions and parameter type annotations
- [~] Stricter semicolon enforcement (parser currently tolerant)
- [~] Arrays/collections + literals + iteration over collections
- [ ] Structs/records beyond current object model

## Error model
- [x] `fallible<T>` with `on error` hooks; panic/yield/return error; stacktrace captured in VM
- [~] Typed errors and finalized stacktrace format
- [ ] Error propagation semantics across functions in compiler/codegen

## Modules/imports
- [x] String-path and RuntimeLibrary imports; package declaration syntax
- [ ] Package search paths/config, stdlib layout, versioning

## Bytecode / VM
- [x] Header (`CODE` + version byte v0x01)
- [x] Opcodes: arithmetic, comparisons, stack ops, jumps, load/store, PRINT
- [x] CALL/RET with per-frame locals
- [~] Constant pool (strings/other literals)
- [ ] Exceptions/error objects in the VM

## Compiler pipeline
- [x] Lexer/parser/AST for current subset
- [x] Codegen to VM bytecode for current subset
- [x] CLI: compile/run/disasm/token-dump; `.code` → `.bytecode`
- [~] Type checker (identifier resolution, type rules, definite assignment)
- [~] Better function frame sizing and temp management
- [~] Stricter semicolon enforcement + richer diagnostics
- [ ] Optimizations (dead code elimination, constant folding)

## Runtime / stdlib
- [~] Basic stdlib (print is builtin opcode; need IO/math/time)
- [ ] REPL

## Tooling / docs
- [x] Bytecode spec v0.3
- [x] AI context + sample programs
- [~] Disassembler/trace polish; additional CLI flags (trace, etc.)
- [ ] Formatter/linter for `.code`

## Testing
- [x] VM harness tests for ops/jumps/load-store/call-ret
- [~] Compiler integration tests from `.code` → bytecode → output
- [ ] Property-based / fuzz testing for parser and VM
