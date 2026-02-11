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
- [x] Function declarations and calls (CALL/RET), typed parameters and returns
- [x] Return statements (implicit 0 if missing)
- [~] Stricter semicolon enforcement (parser currently tolerant)
- [~] Arrays/collections + literals + iteration over collections
- [ ] Structs/records beyond current object model

## Error model
- [x] VM captures runtime IP + call stack; compiler/runtime print source snippets
- [~] Typed errors / exceptions as first-class values in VM
- [ ] Error propagation semantics across functions in compiler/codegen

## Modules/imports
- [ ] Imports / package declaration syntax
- [ ] Package search paths/config, stdlib layout, versioning

## Bytecode / VM
- [x] Header (`CODE` + version byte v0x02) with embedded debug table
- [x] Opcodes: arithmetic, comparisons, stack ops, jumps, load/store, PRINT, CALL/RET, PUSH_STRING
- [~] Constant pool (strings/other literals)
- [ ] Exceptions/error objects in the VM

## Compiler pipeline
- [x] Lexer/parser/AST for current subset
- [x] Codegen to VM bytecode for current subset
- [x] CLI: compile/run/disasm/token-dump; `.code` → `.bytecode`
- [~] Type checker tightening (definite assignment implemented; continue improving return-path and flow analysis)
- [~] Better function frame sizing and temp management
- [~] Stricter semicolon enforcement + richer diagnostics
- [~] Optimizations (initial literal constant folding; broader DCE/CF pending)

## Runtime / stdlib
- [~] Basic stdlib (print opcode exists; need IO/math/time helpers)
- [ ] REPL

## Tooling / docs
- [x] Bytecode spec v0.4 (debug map)
- [x] AI context + sample programs
- [~] Disassembler/trace polish; additional CLI flags (trace, etc.)
- [ ] Formatter/linter for `.code`

## Testing
- [x] VM harness tests for ops/jumps/load-store/call-ret
- [~] Compiler integration tests from `.code` → bytecode → output
- [ ] Property-based / fuzz testing for parser and VM
