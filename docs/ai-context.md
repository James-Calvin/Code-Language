# AI Context — Draive / Code Language
Updated: 2026-02-19

Read this first. Update it whenever semantics or process change.

## Current Capability Snapshot
- Types: integer/whole/real, boolean, string, array<T> (literals `{...}`, `new array<T>(n)`, `.length`, indexing, mutation), optional<T> (`none`, `.hasValue`, `.value`, `.or(fallback)`), constants, typed/void functions, object types.
- Compiler type model: AST/parser/type-checker use `TypeRef`; named object types resolve via object symbol tables (fields + constructors + forward refs).
- Object rules: fields require constructor initialization; constructor/method overloads resolve by typed signature (best-match conversions), methods lower to hidden static call targets with implicit `this`; reserved field names are `length`, `hasValue`, `value`, `or`.
- Interfaces: `interface Name { function<...> method(...); }` plus explicit `implement Interface for Object { method(types...) via Object.method; }` conformance checks and runtime dispatch for interface-typed locals/params/returns/fields.
- Control flow: if/then[/else], while, for, foreach (numeric or array), break/continue, return (implicit 0).
- Expressions: arithmetic (including `%`), comparisons, logical and/or/not (short-circuit), assignment (including `+=`, `-=`, `*=`, `/=`, `%=` and postfix `++/--`), function calls, full-expression string interpolation/concat.
- Time intrinsics: `unix_ms()`, `unix_us()`, `mono_ns()`, `mono_ticks()`, `mono_ticks_per_second()` available as zero-arg global calls.
- Host ABI baseline: compiler lowers `print` and time intrinsics to `HOST_CALL` symbols (`std.io.print`, `std.time.*`), and capability inference includes these lowered features; VM resolves via native/web host binding tables and throws `HostBindingError` on missing/arity mismatch.
- Errors: `panic <expr>;` raises `UserError` with line/col + call stack (from bytecode debug map).
- Bytecode/VM: header v0x05, spec v0.8; ops include strings, arrays (NEW_ARRAY/GET/LEN/SET/NEW_ARRAY_N), optionals (NONE/HAS/VALUE/OR), objects (NEW_OBJECT/GET_FIELD/SET_FIELD/GET_TYPE_NAME), interface dispatch (INTERFACE_CALL), THROW_ERROR.
- Modules/imports: recursive file-based module linking for `.code` files with `import`/`export`, package declarations, grouped/selective imports, module-scope symbol conflict checks, import-chain diagnostics, alias imports for function/object/interface exports, and `lib/` ancestor search.
- Package manifest/lockfile: nearest `code.package.json` is auto-discovered and schema-validated (v1 baseline) with compile-target gating (`targets`) and host capability requirements (`hostAbi.requires`); local dependencies resolve and `code.lock.json` is emitted.
- Library artifact: packages with `kind: "library"` emit `<package>-<version>-<target>.codelib` with embedded bytecode + metadata; resolver validates and prefers artifact paths in lockfile when present.
- Module tooling: `--dump-module-graph [outputPath]` emits entry/modules/import edges; supports text/json/dot output (via `--module-graph-format` or output extension inference); `--trace-linker` emits linker resolution steps.
- Targets/capabilities: `--target vm-native|vm-web` (default `vm-native`) threads through module compilation; linker infers capability groups from package/import namespaces and rejects unsupported target capabilities (e.g., `std.fs` on `vm-web`).
- CLI flags: `--run-tests`, `--skip-tests`, `--disasm`, `--dump-tokens`, `--out`, `--compile-only`, `--dump-module-graph`, `--module-graph-format`, `--trace-linker`, `--target`.
- Tests: integration (core features, arrays, optionals, objects, interfaces, modules/imports, target capability validation, panic) + fuzz (arith, boolean, strings, loop sums, panic). Run `dotnet run --project ConsoleApp1/ConsoleApp1.csproj --run-tests`.

## Not Implemented (yet)
- User-defined data remaining: records, visibility enforcement, broader interface container/module surfaces, and dispatch optimization beyond baseline tables.
- Module namespaces and stdlib versioning/layout.
- Typed array element enforcement, constant pool, formatter/linter, REPL.
- Typed error values / `fallible<T>` semantics wired to VM errors.

## Canonical Docs
- Language spec: `docs/code-language-spec.md`
- Bytecode spec: `docs/bytecode-spec.md`
- Roadmap/status: `docs/features-roadmap.md`
- Platform/package plan: `docs/platform-roadmap.md`
- Examples: `ConsoleApp1/examples/*.code`
- README: build/run quickstart

## Process Expectations
- When changing semantics: update spec, roadmap, bytecode spec (if opcodes), examples, tests. Run `--run-tests`.
- Keep examples comprehensive—add one per new feature.
- Preserve prior decisions; ask user when unclear.

## Change Log
- 2026-02-13: Added `package` declarations, module-level import/declaration conflict checks, and chained import diagnostics (`a -> b -> c`) for linker errors.
- 2026-02-13: Added module graph tooling (`--dump-module-graph`) and linker tracing (`--trace-linker`) with integration coverage.
- 2026-02-13: Added file-based machine-readable module graph export (JSON/DOT) with `--dump-module-graph <file>` and `--module-graph-format` override.
- 2026-02-14: Implemented modulo operator, enhanced assignments (`+=`/`-=`/`*=` `/=` `%=` + postfix `++/--`), constants (`constant`), void function support (`function<void>` and implicit-void `function name(...)`), and full interpolation expression parsing.
- 2026-02-19: Added compile targets (`--target vm-native|vm-web`) and compile-time capability matrix checks inferred from package/import namespaces; expanded integration tests for target acceptance/rejection.
- 2026-02-19: Added package manifest baseline (`code.package.json`) parser/validator with target compatibility checks, host capability validation, and integration tests.
- 2026-02-19: Added baseline package dependency resolver and lockfile generation (`code.lock.json`) with local package discovery, semver range validation, and lockfile integration tests.
- 2026-02-19: Added `.codelib` library artifact format (read/write/validate), automatic artifact emission for library packages, lockfile preference for validated artifacts, and CLI support to run/disassemble `.codelib` inputs.
- 2026-02-19: Added timing intrinsics (unix/us + monotonic ns/ticks) in type checker/codegen/VM with integration coverage and example program.
- 2026-02-19: Added `HOST_CALL` opcode and native host binding table; migrated compiler lowering for `print` + time intrinsics to host ABI symbols (`std.io.*`, `std.time.*`).
- 2026-02-19: Added host-mode parity scaffold (`vm-native` vs `vm-web`) in VM/CLI and parity test coverage for `print` + time host calls.
- 2026-02-13: Implemented module linker MVP (`import`/`export`, alias imports for functions, recursive dependency loading, cycle detection, `lib/` search path) with module integration tests and examples.
- 2026-02-12: Refined method/constructor dispatch to signature-based overload resolution with compile-time binding of call sites.
- 2026-02-12: Added interface declarations and explicit implement blocks with compile-time method-signature and return-type conformance checks; added interface tests/example.
- 2026-02-12: Added interface-typed variable assignment/calls with baseline runtime dispatch lowering and VM `GET_TYPE_NAME` support; expanded interface integration tests.
- 2026-02-12: Expanded interface typing to object fields and switched runtime interface calls to a dedicated dispatch table opcode path (`INTERFACE_CALL`), with new field/dispatch integration tests.
- 2026-02-12: Added object methods (`function` inside `object` + `obj.method(args)`) with temporary lowering to hidden static call targets.
- 2026-02-12: Implemented object construction + field read/write + constructor arity checks + baseline definite field initialization enforcement; added VM object opcodes and object integration tests.
- 2026-02-12: Object symbol-table milestone added (object name/field collection, duplicate checks, field-type validation, forward refs) + integration tests; `object.code` example added.
- 2026-02-12: TypeRef milestone implemented (token-free type representation in parser/AST/type-checker); tests pass.
- 2026-02-11: Arrays (typed, length, indexing), optionals (`none`, hasValue/value/or), panic errors, expanded tests/fuzz; CLI `--run-tests`; docs/README/roadmap updated.
- 2026-02-10: Numeric literal rules, conversions, interpolation, overload order, imports resolution, debug map; control flow/functions; CLI utilities.
- 2026-02-09: Initial context and spec v0.8 snapshot.
