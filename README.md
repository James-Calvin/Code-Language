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
- Enumerations with strongly typed members accessed as `EnumName.Member`
- Records with copy-on-assignment/pass/return semantics for data fields and constructors
- Constants: `constant Type name = value;` (immutable after init)
- Control flow: `if/then/else`, `switch`, `while`, `for`, `foreach` (numeric bounds and arrays), `break`, and `continue`
- Expressions: arithmetic (including `%`), comparisons, logical `and/or/not`, enhanced assignments (`+=`, `-=`, `*=`, `/=`, `%=` and postfix `++/--`) across variables, object fields, array elements, and map entries, plus string interpolation and concatenation
- Time intrinsics: `unix_ms()`, `unix_us()`, `mono_ns()`, `mono_ticks()`, `mono_ticks_per_second()`, `sleep_ms(ms)`
- Math and randomness intrinsics: `minimum()`, `maximum()`, `absolute()`, `sign()`, `lerp()`, `sine()`, `cosine()`, `random()`
- Built-in collections: arrays plus `map<Key, Value>`, `set<Value>`, `queue<Value>`, and `stack<Value>` with shared `.length`
- Typed recoverable errors: `fallible<Value, ErrorCode>` plus prototype-friendly `fallible<Value>`, `return error(code[, message]);`, `return error(message);` for integer-coded fallibles, and `expression on error { ... yield fallback; }`
- Native-only IO intrinsic: `read_line()`
- Functions with CALL/RET, locals, return (implicit 0)
- File modules: top-level declaration visibility (`public`, `package`, `private`) plus member-level visibility for object/record fields, constructors, and methods; legacy `export`; imports (`import Name [as Alias] from "path";`, `import { A, B as C } from "path";`, `import everything as Namespace from "path";`, `export import ...`); package-aware import checks; recursive linking; and `lib/` search
- Package manifest + lockfile baseline: nearest `code.package.json` is parsed/validated during module compile; local dependency graph resolves and `code.lock.json` is generated
- Host ABI baseline: compiler emits `HOST_CALL` for `print`, time/math intrinsics, native-only APIs (`standard.input_output.read_line`, `std.time.sleep_ms`), and engine stubs (`engine.window/*`, `engine.input/*`, `engine.gfx/*`)
- Web app build/runtime V1 slice: `--build-web` emits a runnable static site folder with `index.html`, `app.bytecode`, copied `assets/` content when present, a full-bleed canvas runtime, `MainScene` scene-object lifecycle (`start/update/draw` plus optional `draw_hud()`), guaranteed `640x360` safe area, hybrid-expand world framing, HUD screen-space, and browser-backed `key_down()`/`clear()`/`draw_rectangle()`/`draw_rectangle_outline()`/`draw_line()`/`draw_circle()`/`draw_circle_outline()`/`draw_polygon()`/`draw_polygon_outline()`/`draw_text()`/`draw_image()`/`draw_sprite()`
- Higher-level engine wrapper layer: root `lib/engine/` modules now provide `engine.colors`, `engine.drawing`, `engine.input`, `engine.viewport`, and `engine.scene`, with compatibility modules `engine.view` and `engine.loop`, including explicit child-object scene composition over split lifecycle interfaces
- Browser runtime harness (`web-runtime/`): lower-level JavaScript VM harness for loading raw `.bytecode` / `.codelib` files during bring-up and debugging
- Runtime diagnostics: bytecode debug map -> line/column stack traces
- Error handling: `panic <expr>;` emits an unrecoverable `UserError` with call stack; `fallible<Value, ErrorCode>` handles expected recoverable failures

See [the language spec](docs/code-language-spec.md), [the feature roadmap](docs/features-roadmap.md), and [the web app/runtime V1 contract](docs/web-app-v1.md) for the current scope and the target developer workflow.

## Implemented Today vs Planned
- Implemented today: enumerations, records, `switch`, `break`/`continue`, numeric base prefixes, escaped interpolation braces, objects, interfaces, arrays, built-in collections, optionals, typed recoverable `fallible<Value, ErrorCode>` errors plus `fallible<Value>` shorthand, time/math intrinsics, grouped/selective/namespace/re-export imports, package manifests/lockfiles/library artifacts, target capability checks, `panic`, and the current web build/runtime slice.
- Implemented today: top-level module visibility modifiers (`public`, `package`, `private`) with legacy `export` compatibility, plus member-level visibility for object/record fields, constructors, and methods.
- Planned, not implemented today: propagation shorthand for fallible errors.
- Example status and usage live in [the example catalog](docs/example-catalog.md).

## Current State vs Target Workflow
- Current state: scene-object web apps can now be built with `--build-web` into a runnable static site folder, defaulting to `dist/`.
- Current state: the generated browser runtime owns the canvas, fills the window edge-to-edge, preserves aspect ratio with a guaranteed `640x360` safe area, expands the visible world on wider/taller screens, and supports `MainScene.start()`, `update()`, `draw()`, optional `draw_hud()`, `key_down()`, `clear()`, `draw_rectangle()`, `draw_rectangle_outline()`, `draw_line()`, `draw_circle()`, `draw_circle_outline()`, `draw_polygon()`, `draw_polygon_outline()`, `draw_text()`, `draw_image()`, `draw_sprite()`, `camera_view_*()`, `camera_safe_*()`, `screen_width()`, and `screen_height()`.
- Current state: the repo also ships a wrapper layer in `lib/engine/` so scene apps can import `engine.colors`, `engine.drawing`, `engine.input`, `engine.viewport`, and `engine.scene` instead of calling the raw helpers directly. `engine.view` and `engine.loop` remain as compatibility modules during the migration pass.
- Current state: larger apps can now keep behavior in separate child objects and explicitly register them with `Scene` through `Startable`, `Updatable`, `WorldDrawable`, and `HudDrawable`.
- Current state: function-heavy engine modules can now be imported as compile-time namespaces with `import everything as Draw from "engine/drawing.code";`, and interfaces can be implemented inline inside object bodies.
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
Current web build behavior:
- Copies an `assets/` directory from the package root when a manifest exists, otherwise from the entry file directory.
- Preserves relative asset paths such as `assets/code-sheet.svg` for `draw_image()` / `draw_sprite()`.

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
Test harness covers VM ops, compiler integration (print, arithmetic, functions, loops, foreach, strings, interfaces, modules/imports, fallible errors, panic), and fuzz suites (arithmetic, boolean, strings, loop sums, panic).

## Project conventions
- Source files end with `.code`; compiled bytecode uses `.bytecode`
- Library artifacts use `.codelib` (`<package>-<version>-<target>.codelib`)
- Semicolons required; `then` mandatory after `if/while/for/foreach` conditions
- `switch` syntax: `switch value then { case expr then statement ... default then statement }`; no fallthrough
- Function returns: explicit `function<T> name(...)` or implicit-void `function name(...)`
- Naming rule: user-facing APIs prefer fully spelled-out words; accepted domain terms like `hud` remain allowed
- Arrays: literals `{...}`, typed declarations `array<integer> xs = {1,2,3};`, dynamic `new array<integer>(n);`, `.length`, indexing `xs[i]`, mutation `xs[i] = value`, and growable methods `xs.append(value)` / `xs.remove_at(index)`
- Built-in collections: `map<Key, Value>` with `items[key]`, `items[key] = value`, `contains(key)`, `remove(key)`; `set<Value>` with `add`, `contains`, `remove`; `queue<Value>` with `enqueue`, `dequeue`, `peek`; `stack<Value>` with `push`, `pop`, `peek`; all expose `.length`
- Enumerations: `enum Name { Member; Other = 5; }` with strongly typed values accessed as `Name.Member`
- Records: `record Name { ... }` with constructors, methods, inline/external interface implementations, structural equality for hashable records, and value semantics across assignment, parameter passing, returns, and container insertion. Record methods receive a copied `this`, so persistent changes use a return-and-reassign pattern.
- Optionals: `optional<T>` with `none`, `.hasValue`, `.value`, `.or(fallback)`
- Fallible recoverable errors: `fallible<Value, ErrorCode>` where `ErrorCode` is an enum or `integer`; `fallible<Value>` is shorthand for `fallible<Value, integer>`; functions may `return` a plain success value, `return error(code);`, `return error(code, message);`, or `return error(message);` for integer-coded fallibles (code `0`); callers unwrap with `expr on error { ... yield fallback; }`; handler code can read `error.code` and `error.message`; `fallible<void, E>` and propagation shorthand are deferred
- Objects: `object` declarations with constructors/methods, `new Type(...)`, field access/assignment (`obj.field`, `obj.field = ...`), method calls (`obj.method(...)`), member-level `public` / `package` / `private` visibility, and implicit `this` lookup inside object bodies for unshadowed fields and bare method calls
- Interfaces: `interface` declarations + inline interface methods (`implement Interface.method(...) { ... }`) or explicit `implement Interface for Object { ... via Object.method; }` conformance checks
- Interface-typed locals/params/returns/fields/arrays and runtime-dispatched interface method calls
- Modules: top-level `public`, `package`, and `private` declaration visibility; matching packages also gate `package` members; legacy `export` remains a compatibility alias for `public`
- Imports: `import Name [as Alias] from "path";`
- Grouped/selective imports: `import { add, sub as minus } from "math.code";`
- Namespace imports: `import everything as Draw from "engine/drawing.code";` for function-only module surfaces
- Re-export imports: `export import Name from "path";`, `export import { A, B } from "path";`
- Package declarations: optional `package Name;` at top of module (before imports/declarations); matching package names enable `package`-visible imports
- Package manifest: optional `code.package.json` (nearest ancestor) with validated fields (`schemaVersion`, `name`, `version`, `kind`, `entry`, optional `targets`, `targetOverrides`, `hostAbi.requires`, deps maps); `targetOverrides` are currently schema-validated but not yet used to auto-select a different entry file at compile time
- Lockfile: `code.lock.json` is written in the package root during compile when a manifest is present (schema v1, target, resolved package list with integrity hashes)
- Library packages (`kind: "library"`) emit a `.codelib` artifact during compile; lockfile resolution prefers `.codelib` paths when present and validated
- Import resolution: importing file directory first, then discovered ancestor `lib/` folders
- Current engine wrapper modules live under `lib/engine/` and are imported as `"engine/colors.code"`, `"engine/drawing.code"`, `"engine/input.code"`, `"engine/viewport.code"`, and `"engine/scene.code"`; `"engine/view.code"` and `"engine/loop.code"` remain as compatibility re-export modules
- Alias imports support exported functions, objects, interfaces, and enumerations
- Module tooling flags: `--dump-module-graph [outputPath]`, `--module-graph-format <text|json|dot>`, and `--trace-linker`
- Compile target flag: `--target vm-native|vm-web` (default `vm-native`)
- Capability checks: package/import namespaces under `std.*` and `engine.*` are validated against the selected target (e.g., `std.fs` is rejected on `vm-web`)
- Native-only host APIs are rejected on web target at compile time (`read_line`, `sleep_ms`) and raise target-specific `HostBindingError` if forced at runtime
- Runtime host mode follows `--target` when executing `.code`/`.bytecode`/`.codelib` in CLI (`vm-native` vs `vm-web` host binding table)
- Linker diagnostics include import chains for cycles/missing exports
- Method/constructor overload resolution: compile-time signature-based dispatch (with best-match conversions)
- Constructor rules: objects with fields must define constructors, and constructors must assign all fields
- Errors are reported with line/column and call stack when possible
- Interface-typed arrays now participate in type checking, indexing, `foreach`, and runtime dispatch; `foreach` support for the newer built-in collections is still deferred

## Roadmap (high level)
Active priorities: grow the current `lib/engine/` wrapper layer beyond scene composition into a fuller engine-facing API, continue replacing raw engine stubs with real browser-backed implementations, and expand the browser runtime beyond the current primitives/keyboard/image-sprite slice into richer rendering, input, and audio. The web runtime remains JavaScript for now; a Wasm path is deferred until performance or parity data justifies the extra toolchain cost. Full detail is in [docs/features-roadmap.md](docs/features-roadmap.md) and [docs/platform-roadmap.md](docs/platform-roadmap.md).

## Examples
See [docs/example-catalog.md](docs/example-catalog.md) for the implementation-truth catalog of runnable, negative, and planned examples.
