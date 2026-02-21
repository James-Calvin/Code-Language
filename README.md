# Code (Work-in-Progress)

A tiny experimental programming language with a stack-based bytecode VM and a C# compiler pipeline. The repo contains:
- `ConsoleApp1/` — compiler, VM, disassembler, and test harness
- `docs/` — language spec drafts, roadmap, AI context
- `examples/` — sample `.code` programs

## Features (implemented)
- Typed variables/functions; primitives: integer/whole/real, boolean, string
- Constants: `constant Type name = value;` (immutable after init)
- Control flow: `if/then/else`, `while`, `for`, `foreach` (numeric bounds and arrays)
- Expressions: arithmetic (including `%`), comparisons, logical `and/or/not`, enhanced assignments (`+=`, `-=`, `*=`, `/=`, `%=` and postfix `++/--`), string interpolation and concatenation
- Time intrinsics: `unix_ms()`, `unix_us()`, `mono_ns()`, `mono_ticks()`, `mono_ticks_per_second()`
- Functions with CALL/RET, locals, return (implicit 0)
- File modules: `export` + imports (`import Name [as Alias] from "path";`, `import { A, B as C } from "path";`) with recursive linking and `lib/` search
- Package manifest + lockfile baseline: nearest `code.package.json` is parsed/validated during module compile; local dependency graph resolves and `code.lock.json` is generated
- Host ABI baseline: compiler emits `HOST_CALL` for `print`/time intrinsics (`std.io.print`, `std.time.*`), VM resolves through runtime host bindings
- Runtime diagnostics: bytecode debug map → line/column stack traces
- Error objects: `panic <expr>;` emits a `UserError` with call stack

See `docs/code-language-spec.md` and `docs/features-roadmap.md` for current scope and priorities.

## Building
```
dotnet build
```

## Running programs
Compile and run a `.code` file:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- path/to/file.code
```
Or run a bytecode directly:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- path/to/file.bytecode
```
Or run a library artifact directly:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- path/to/file.codelib
```
Disassemble a bytecode file:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --disasm path/to/file.bytecode
```
Disassemble a library artifact:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --disasm path/to/file.codelib
```
Compile and print module graph/linker trace:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --compile-only --dump-module-graph --trace-linker path/to/file.code
```
Compile for a specific target:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --target vm-web --compile-only path/to/file.code
```
Write module graph to file (format inferred from extension: `.json`, `.dot`, `.gv`; default text):
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --compile-only --dump-module-graph graph.json path/to/file.code
```
Force graph format explicitly:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --compile-only --dump-module-graph graph.txt --module-graph-format dot path/to/file.code
```

## Tests
- Default (no args) used to run tests; now use the explicit flag:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj --run-tests
```
Test harness covers VM ops, compiler integration (print, arithmetic, functions, loops, foreach, strings, interfaces, modules/imports, panic), and fuzz suites (arithmetic, boolean, strings, loop sums, panic).

## Project conventions
- Source files end with `.code`; compiled bytecode uses `.bytecode`
- Library artifacts use `.codelib` (`<package>-<version>-<target>.codelib`)
- Semicolons required; `then` mandatory after `if/while/for/foreach` conditions
- Function returns: explicit `function<T> name(...)` or implicit-void `function name(...)`
- Arrays: literals `{...}`, typed declarations `array<integer> xs = {1,2,3};`, dynamic `new array<integer>(n);`, `.length`, indexing `xs[i]`, mutation `xs[i] = value`
- Optionals: `optional<T>` with `none`, `.hasValue`, `.value`, `.or(fallback)`
- Objects: `object` declarations with constructors/methods, `new Type(...)`, field access/assignment (`obj.field`, `obj.field = ...`), method calls (`obj.method(...)`)
- Interfaces: `interface` declarations + explicit `implement Interface for Object { ... via Object.method; }` conformance checks
- Interface-typed locals/params/returns/fields and runtime-dispatched interface method calls
- Modules: `export` for top-level function/object/interface declarations; `import Name [as Alias] from "path";`
- Grouped/selective imports: `import { add, sub as minus } from "math.code";`
- Package declarations: optional `package Name;` at top of module (before imports/declarations)
- Package manifest: optional `code.package.json` (nearest ancestor) with validated fields (`schemaVersion`, `name`, `version`, `kind`, `entry`, optional `targets`, `targetOverrides`, `hostAbi.requires`, deps maps)
- Lockfile: `code.lock.json` is written in the package root during compile when a manifest is present (schema v1, target, resolved package list with integrity hashes)
- Library packages (`kind: "library"`) emit a `.codelib` artifact during compile; lockfile resolution prefers `.codelib` paths when present and validated
- Import resolution: importing file directory first, then discovered ancestor `lib/` folders
- Alias imports support exported functions, objects, and interfaces
- Module tooling flags: `--dump-module-graph [outputPath]`, `--module-graph-format <text|json|dot>`, and `--trace-linker`
- Compile target flag: `--target vm-native|vm-web` (default `vm-native`)
- Capability checks: package/import namespaces under `std.*` and `engine.*` are validated against the selected target (e.g., `std.fs` is rejected on `vm-web`)
- Linker diagnostics include import chains for cycles/missing exports
- Method/constructor overload resolution: compile-time signature-based dispatch (with best-match conversions)
- Constructor rules: objects with fields must define constructors, and constructors must assign all fields
- Errors are reported with line/column and call stack when possible
- Current interface dispatch scope: direct interface-typed values (locals/params/returns/fields); broader container/module surfaces are still planned

## Roadmap (high level)
Active priorities: optimization of temp/liveness, collections over real data structures, modules/imports, stdlib growth, and tooling (formatter/linter). Full detail in `docs/features-roadmap.md`.

## Examples
Compile + run an example:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- ConsoleApp1/examples/arithmetic.code
```

Module import example:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- ConsoleApp1/examples/modules/main.code
```

Time intrinsics example:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- ConsoleApp1/examples/time.code
```

Panic example:
```
panic("boom");
```
