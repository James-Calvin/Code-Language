# AI Context — Draive / Code Language
Updated: 2026-02-12

Read this first. Update it whenever semantics or process change.

## Current Capability Snapshot
- Types: integer/whole/real, boolean, string, array<T> (literals `{...}`, `new array<T>(n)`, `.length`, indexing, mutation), optional<T> (`none`, `.hasValue`, `.value`, `.or(fallback)`), typed functions, object types.
- Compiler type model: AST/parser/type-checker use `TypeRef`; named object types resolve via object symbol tables (fields + constructors + forward refs).
- Object rules: fields require constructor initialization; constructor/method overloads are arity-based; methods lower to hidden static call targets with implicit `this`; reserved field names are `length`, `hasValue`, `value`, `or`.
- Control flow: if/then[/else], while, for, foreach (numeric or array), break/continue, return (implicit 0).
- Expressions: arithmetic, comparisons, logical and/or/not (short-circuit), assignment, function calls, string interpolation/concat.
- Errors: `panic <expr>;` raises `UserError` with line/col + call stack (from bytecode debug map).
- Bytecode/VM: header v0x05, spec v0.8; ops include strings, arrays (NEW_ARRAY/GET/LEN/SET/NEW_ARRAY_N), optionals (NONE/HAS/VALUE/OR), objects (NEW_OBJECT/GET_FIELD/SET_FIELD), THROW_ERROR.
- CLI flags: `--run-tests`, `--skip-tests`, `--disasm`, `--dump-tokens`, `--out`, `--compile-only`.
- Tests: integration (core features, arrays, optionals, objects, panic) + fuzz (arith, boolean, strings, loop sums, panic). Run `dotnet run --project ConsoleApp1/ConsoleApp1.csproj --run-tests`.

## Not Implemented (yet)
- User-defined data remaining: records, interfaces, visibility enforcement, dynamic/interface dispatch.
- Modules/imports/package layout and stdlib organization.
- Typed array element enforcement, constant pool, formatter/linter, REPL.
- Typed error values / `fallible<T>` semantics wired to VM errors.

## Canonical Docs
- Language spec: `docs/code-language-spec.md`
- Bytecode spec: `docs/bytecode-spec.md`
- Roadmap/status: `docs/features-roadmap.md`
- Examples: `ConsoleApp1/examples/*.code`
- README: build/run quickstart

## Process Expectations
- When changing semantics: update spec, roadmap, bytecode spec (if opcodes), examples, tests. Run `--run-tests`.
- Keep examples comprehensive—add one per new feature.
- Preserve prior decisions; ask user when unclear.

## Change Log
- 2026-02-12: Added object methods (`function` inside `object` + `obj.method(args)`) with temporary lowering to hidden static call targets.
- 2026-02-12: Implemented object construction + field read/write + constructor arity checks + baseline definite field initialization enforcement; added VM object opcodes and object integration tests.
- 2026-02-12: Object symbol-table milestone added (object name/field collection, duplicate checks, field-type validation, forward refs) + integration tests; `object.code` example added.
- 2026-02-12: TypeRef milestone implemented (token-free type representation in parser/AST/type-checker); tests pass.
- 2026-02-11: Arrays (typed, length, indexing), optionals (`none`, hasValue/value/or), panic errors, expanded tests/fuzz; CLI `--run-tests`; docs/README/roadmap updated.
- 2026-02-10: Numeric literal rules, conversions, interpolation, overload order, imports resolution, debug map; control flow/functions; CLI utilities.
- 2026-02-09: Initial context and spec v0.8 snapshot.
