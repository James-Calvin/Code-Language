# Code (Work-in-Progress)

A tiny experimental programming language with a stack-based bytecode VM and a C# compiler pipeline. The repo contains:
- `ConsoleApp1/` — compiler, VM, disassembler, and test harness
- `docs/` — language spec drafts, roadmap, AI context
- `examples/` — sample `.code` programs

## Features (implemented)
- Typed variables/functions; primitives: integer/whole/real, boolean, string
- Control flow: `if/then/else`, `while`, `for`, `foreach` (numeric bounds and arrays)
- Expressions: arithmetic, comparisons, logical `and/or/not`, string interpolation and concatenation
- Functions with CALL/RET, locals, return (implicit 0)
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
Disassemble a bytecode file:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --disasm path/to/file.bytecode
```

## Tests
- Default (no args) used to run tests; now use the explicit flag:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj --run-tests
```
Test harness covers VM ops, compiler integration (print, arithmetic, functions, loops, foreach, strings, interfaces, panic), and fuzz suites (arithmetic, boolean, strings, loop sums, panic).

## Project conventions
- Source files end with `.code`; compiled bytecode uses `.bytecode`
- Semicolons required; `then` mandatory after `if/while/for/foreach` conditions
- Arrays: literals `{...}`, typed declarations `array<integer> xs = {1,2,3};`, dynamic `new array<integer>(n);`, `.length`, indexing `xs[i]`, mutation `xs[i] = value`
- Optionals: `optional<T>` with `none`, `.hasValue`, `.value`, `.or(fallback)`
- Objects: `object` declarations with constructors/methods, `new Type(...)`, field access/assignment (`obj.field`, `obj.field = ...`), method calls (`obj.method(...)`)
- Interfaces: `interface` declarations + explicit `implement Interface for Object { ... via Object.method; }` conformance checks
- Interface-typed local variables and runtime-dispatched interface method calls
- Method/constructor overload resolution: compile-time signature-based dispatch (with best-match conversions)
- Constructor rules: objects with fields must define constructors, and constructors must assign all fields
- Errors are reported with line/column and call stack when possible
- Current interface dispatch scope: interface-typed locals and calls (broader surfaces like fields/modules are still planned)

## Roadmap (high level)
Active priorities: optimization of temp/liveness, collections over real data structures, modules/imports, stdlib growth, and tooling (formatter/linter). Full detail in `docs/features-roadmap.md`.

## Examples
Compile + run an example:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- ConsoleApp1/examples/arithmetic.code
```

Panic example:
```
panic("boom");
```
