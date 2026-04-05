# Code (Work-in-Progress)

`Code` is an experimental, code-first programming language aimed at building 2D interactive applications for the web. The current repo is still a compiler/runtime prototype built around a stack-based bytecode VM and a C# toolchain, but the near-term product direction is clear:
- write Code source
- build once
- get a deployable website

The repo contains:
- `ConsoleApp1/` - compiler, VM, disassembler, and test harness
- `docs/` - language spec drafts, roadmap, AI context
- `ConsoleApp1/examples/` - sample `.code` programs

## Features (implemented)
- Typed variables/functions; primitives: integer/whole/real, boolean, string
- Constants: `constant Type name = value;` (immutable after init)
- Control flow: `if/then/else`, `while`, `for`, `foreach` (numeric bounds and arrays)
- Expressions: arithmetic (including `%`), comparisons, logical `and/or/not`, enhanced assignments (`+=`, `-=`, `*=`, `/=`, `%=` and postfix `++/--`) across variables, object fields, and array elements, plus string interpolation and concatenation
- Time intrinsics: `unix_ms()`, `unix_us()`, `mono_ns()`, `mono_ticks()`, `mono_ticks_per_second()`, `sleep_ms(ms)`
- Native-only IO intrinsic: `read_line()`
- Functions with CALL/RET, locals, return (implicit 0)
- File modules: `export` + imports (`import Name [as Alias] from "path";`, `import { A, B as C } from "path";`) with recursive linking and `lib/` search
- Package manifest + lockfile baseline: nearest `code.package.json` is parsed/validated during module compile; local dependency graph resolves and `code.lock.json` is generated
- Host ABI baseline: compiler emits `HOST_CALL` for `print`, time intrinsics, native-only APIs (`standard.input_output.read_line`, `std.time.sleep_ms`), and engine stubs (`engine.window/*`, `engine.input/*`, `engine.gfx/*`)
- Web app build/runtime V1 slice: `--build-web` emits a runnable static site folder with `index.html`, `app.bytecode`, a full-bleed canvas runtime, `MainScene` scene-object lifecycle (`start/update/draw` plus optional `draw_hud()`), guaranteed `640x360` safe area, hybrid-expand world framing, HUD screen-space, and browser-backed `key_down()`/`clear()`/`draw_rectangle()`/`draw_line()`/`draw_text()`
- Browser runtime harness (`web-runtime/`): lower-level JavaScript VM harness for loading raw `.bytecode` / `.codelib` files during bring-up and debugging
- Runtime diagnostics: bytecode debug map -> line/column stack traces
- Error objects: `panic <expr>;` emits a `UserError` with call stack

See [the language spec](docs/code-language-spec.md), [the feature roadmap](docs/features-roadmap.md), and [the web app/runtime V1 contract](docs/web-app-v1.md) for the current scope and the target developer workflow.

## Current State vs Target Workflow
- Current state: scene-object web apps can now be built with `--build-web` into a runnable static site folder, defaulting to `dist/`.
- Current state: the generated browser runtime owns the canvas, fills the window edge-to-edge, preserves aspect ratio with a guaranteed `640x360` safe area, expands the visible world on wider/taller screens, and supports `MainScene.start()`, `update()`, `draw()`, optional `draw_hud()`, `key_down()`, `clear()`, `draw_rectangle()`, `draw_line()`, `draw_text()`, `camera_view_*()`, `camera_safe_*()`, `screen_width()`, and `screen_height()`.
- Current state: `web-runtime/index.html` still exists as a lower-level preview/debug harness for raw `.bytecode` / `.codelib` loading.
- Target workflow: expand this slice into higher-level engine packages and richer rendering/input/audio without forcing raw window-handle management into the default authoring model.

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
Compile and run using web host bindings:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --target vm-web path/to/file.code
```
Compile for web and run in the current preview browser harness:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --target vm-web --compile-only path/to/file.code
# then load the generated .bytecode in web-runtime/index.html
```
Build a web app:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --build-web ConsoleApp1/examples/web_scene.code
```
Build a web app to a custom output directory:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --build-web --out path/to/dist ConsoleApp1/examples/web_scene.code
```
Current limitation: `--build-web` does not yet combine with `--dump-module-graph`.

The preview harness remains useful for raw bytecode bring-up, but the primary browser workflow is now `--build-web`. The current runtime contract is documented in [docs/web-app-v1.md](docs/web-app-v1.md).
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
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --run-tests
```
Test harness covers VM ops, compiler integration (print, arithmetic, functions, loops, foreach, strings, interfaces, modules/imports, panic), and fuzz suites (arithmetic, boolean, strings, loop sums, panic).

## Project conventions
- Source files end with `.code`; compiled bytecode uses `.bytecode`
- Library artifacts use `.codelib` (`<package>-<version>-<target>.codelib`)
- Semicolons required; `then` mandatory after `if/while/for/foreach` conditions
- Function returns: explicit `function<T> name(...)` or implicit-void `function name(...)`
- Naming rule: user-facing APIs prefer fully spelled-out words; accepted domain terms like `hud` remain allowed
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
- Native-only host APIs are rejected on web target at compile time (`read_line`, `sleep_ms`) and raise target-specific `HostBindingError` if forced at runtime
- Runtime host mode follows `--target` when executing `.code`/`.bytecode`/`.codelib` in CLI (`vm-native` vs `vm-web` host binding table)
- Linker diagnostics include import chains for cycles/missing exports
- Method/constructor overload resolution: compile-time signature-based dispatch (with best-match conversions)
- Constructor rules: objects with fields must define constructors, and constructors must assign all fields
- Errors are reported with line/column and call stack when possible
- Current interface dispatch scope: direct interface-typed values (locals/params/returns/fields); broader container/module surfaces are still planned

## Roadmap (high level)
Active priorities: expand the generated web app/runtime slice into higher-level engine packages, continue replacing raw engine stubs with real browser-backed implementations, and grow the rendering/input surface beyond rectangles + keyboard. Full detail is in [docs/features-roadmap.md](docs/features-roadmap.md) and [docs/platform-roadmap.md](docs/platform-roadmap.md).

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

Engine host stub example:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --target vm-web ConsoleApp1/examples/engine_stubs.code
```

Web scene build example:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --build-web ConsoleApp1/examples/web_scene.code
```

Native-only host API example:
```
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- ConsoleApp1/examples/host_abi_native_only.code
```

Panic example:
```
panic("boom");
```

