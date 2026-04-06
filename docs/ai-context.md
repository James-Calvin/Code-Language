# AI Context - Draive / Code Language
Updated: 2026-04-05

Read this first. Update it whenever semantics or process change.

## Strategic Direction (active)
- Web-first for code-first 2D interactive applications and games, while preserving native portability.
- Keep language-facing engine APIs backend-agnostic; host bindings provide platform-specific implementations.
- Current web JS runtime is a bootstrap/prototyping runtime; it does not block a future WebGPU or WASM-hosted VM path.
- Capability query + explicit fallback policy is a required design constraint for predictable cross-target behavior.
- Near-term product target: write Code source, build once, and get a deployable website.
- Default web runtime target: full-window browser app, centered `640x360` safe area with hybrid-expanded world framing, scene-object authoring, separate HUD space, a first wrapper layer in `lib/engine/`, and browser-backed drawing/image-sprite/keyboard support for V1.

## Current Capability Snapshot
- Types: integer/whole/real, boolean, string, enums (`enum Name { Member; Other = 5; }` with `Enum.Member` access and strong enum-to-enum typing), records (`record Name { ... }` with constructors, field access, and copy-on-assignment/pass/return semantics), array<T> (literals `{...}`, `new array<T>(n)`, `.length`, indexing, mutation, `append`, `remove_at`, preserved element typing through indexing/foreach), built-in collections (`map<Key, Value>`, `set<Value>`, `queue<Value>`, `stack<Value>` with shared `.length` and collection-specific methods), optional<T> (`none`, `.hasValue`, `.value`, `.or(fallback)`), constants, typed/void functions, and object types. User-facing `fallible<T>` syntax is not implemented.
- Compiler type model: AST/parser/type-checker use `TypeRef`; named object types resolve via object symbol tables (fields + constructors + forward refs).
- Object rules: fields require constructor initialization; constructor/method overloads resolve by typed signature (best-match conversions), methods lower to hidden static call targets with implicit `this`, object bodies support implicit field access / implicit `this` method calls when names are not shadowed by locals or parameters, and object methods support implicit-void authoring; reserved field names are `length`, `hasValue`, `value`, `or`.
- Record rules: records support fields, constructors, methods, inline/external interface implementations, structural equality for hashable records, and use as `map` keys / `set` elements when hashable; assignments, parameter passing, returns, and collection insertion clone record values; nested record fields and `optional<Record>` fields clone deeply; record methods clone `this` at method entry, so persistent updates use return-and-reassign.
- Interfaces: `interface Name { function<...> method(...); }` plus either inline object-body implementations `implement Interface.method(...) { ... }` or explicit `implement Interface for Object { method(types...) via Object.method; }`, with runtime dispatch for interface-typed locals/params/returns/fields/arrays.
- Control flow: if/then[/else], switch (no fallthrough), while, for, foreach (numeric or array), break/continue, return (implicit 0).
- Expressions: arithmetic (including `%`), comparisons, logical and/or/not (short-circuit), assignment (including `+=`, `-=`, `*=`, `/=`, `%=` and postfix `++/--` across variables, object fields, and array elements), function calls, full-expression string interpolation/concat.
- Time and math intrinsics: `unix_ms()`, `unix_us()`, `mono_ns()`, `mono_ticks()`, `mono_ticks_per_second()`, `minimum()`, `maximum()`, `absolute()`, `sign()`, `lerp()`, `sine()`, `cosine()`, `random()`, plus native-only `sleep_ms(integer)`.
- IO intrinsic: `read_line() -> string` (native-only).
- Host ABI baseline: compiler lowers print/time/math/native-only/engine intrinsic calls to `HOST_CALL` symbols; capability inference includes lowered host features (`standard.input_output.read_line`, `std.time.sleep_ms`, `std.math`, `engine.window/input/gfx`); VM resolves via native/web host binding tables and throws target-aware `HostBindingError` on unsupported host calls.
- Engine ABI status: legacy window-handle calls (`window_create`, `window_should_close`, `window_present`, `input_key_down`, `gfx_clear`, `gfx_draw_rect`) remain no-op stubs on native/web hosts, while the scene-runtime intrinsics `key_down`, `clear`, `draw_rectangle`, `draw_rectangle_outline`, `draw_line`, `draw_circle`, `draw_circle_outline`, `draw_polygon`, `draw_polygon_outline`, `draw_text`, `draw_image`, `draw_sprite`, `camera_view_*`, `camera_safe_*`, `screen_width`, and `screen_height` now have browser-backed implementations for `vm-web`.
- Web app/runtime slice: `--build-web` emits a static site folder (`index.html` + `app.bytecode`, plus copied `assets/` content when present) with a generated full-window browser runtime, centered `640x360` safe area, hybrid-expanded visible world, `MainScene` metadata, and `start/update/draw` plus optional `draw_hud` driving the JS VM.
- Engine wrapper layer: root `lib/engine/` currently provides canonical `engine.colors`, `engine.drawing`, `engine.input`, `engine.viewport`, and `engine.scene` modules over the raw scene-runtime helpers, with `engine.view` and `engine.loop` retained as compatibility re-exports.
- Scene composition: canonical `engine.scene` now provides explicit child-object registration through `Startable`, `Updatable`, `WorldDrawable`, `HudDrawable`, `Scene`, and `SceneLoop`; compatibility module `engine.loop` remains as a re-export while examples/docs migrate.
- Web harness: `web-runtime/` still contains a lower-level JavaScript bytecode runner + browser host binding table for raw `.bytecode` / `.codelib` loading; it is preview/bootstrap tooling, not the primary shipping workflow.
- Web app/runtime contract: `docs/web-app-v1.md` freezes the first implementation target around a scene object with `start/update/draw`, optional `draw_hud`, full-window browser runtime ownership, centered `640x360` safe area, hybrid-expanded framing, and static-site output.
- Errors: `panic <expr>;` raises `UserError` with line/col + call stack (from bytecode debug map).
- Bytecode/VM: header v0x05, spec v0.9; ops include strings, arrays (NEW_ARRAY/GET/LEN/SET/NEW_ARRAY_N/APPEND/REMOVE_AT), built-in collections (`NEW_MAP`/`MAP_*`, `NEW_SET`/`SET_*`, `NEW_QUEUE`/`QUEUE_*`, `NEW_STACK`/`STACK_*`), optionals (NONE/HAS/VALUE/OR), objects/records (NEW_OBJECT/NEW_RECORD/GET_FIELD/SET_FIELD/GET_TYPE_NAME), interface dispatch (INTERFACE_CALL), and THROW_ERROR.
- Modules/imports: recursive file-based module linking for `.code` files with `import`/`export`, package declarations, grouped/selective imports, namespace imports, re-export imports, module-scope symbol conflict checks, import-chain diagnostics, alias imports for function/object/interface/enum exports, and `lib/` ancestor search.
- Package manifest/lockfile: nearest `code.package.json` is auto-discovered and schema-validated (v1 baseline) with compile-target gating (`targets`), validated `targetOverrides`, and host capability requirements (`hostAbi.requires`); local dependencies resolve and `code.lock.json` is emitted. `targetOverrides` are not yet used to auto-select a different compile entry.
- Library artifact: packages with `kind: "library"` emit `<package>-<version>-<target>.codelib` with embedded bytecode + metadata; resolver validates and prefers artifact paths in lockfile when present.
- Module tooling: `--dump-module-graph [outputPath]` emits entry/modules/import edges; supports text/json/dot output (via `--module-graph-format` or output extension inference); `--trace-linker` emits linker resolution steps.
- Targets/capabilities: `--target vm-native|vm-web` (default `vm-native`) threads through module compilation; linker infers capability groups from package/import namespaces and rejects unsupported target capabilities (e.g., `std.fs` on `vm-web`).
- CLI flags: `--run-tests`, `--skip-tests`, `--disasm`, `--dump-tokens`, `--out`, `--compile-only`, `--build-web`, `--dump-module-graph`, `--module-graph-format`, `--trace-linker`, `--target`.
- Tests: integration (core features, arrays, optionals, objects, interfaces, modules/imports, host ABI surfaces, target capability validation, panic) + fuzz (arith, boolean, strings, loop sums, panic). Run `dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --run-tests`.

## Not Implemented (yet)
- Language gaps: visibility enforcement and user-facing `fallible<T>` / `on error`.
- Stdlib/runtime gaps: broader standard-library surface beyond the current math/random and core container baseline.
- Dispatch/runtime polish: optimization beyond baseline interface dispatch tables.
- Module namespaces and stdlib versioning/layout.
- Constant pool, formatter/linter, REPL.
- Engine web runtime maturation: broaden the current `lib/engine/` wrapper layer beyond colors/drawing/input/viewport, add richer input/audio/content handling, and replace the remaining raw window-handle `engine.window`/`engine.input`/`engine.gfx` stubs with real browser-backed behavior or wrappers.
- Web runtime packaging stance: generated web apps currently inline the JavaScript VM/runtime into `index.html`; Wasm is explicitly deferred until measured performance, parity, or startup-size data justifies the extra complexity.
- GPU roadmap: `engine.gpu` ABI v1 + WebGPU backend (`vm-web`) + native GPU backend parity (`vm-native`).
- Capability query/fallback APIs for deterministic backend negotiation.

## Canonical Docs
- Language spec: `docs/code-language-spec.md`
- Bytecode spec: `docs/bytecode-spec.md`
- Roadmap/status: `docs/features-roadmap.md`
- Platform/package plan: `docs/platform-roadmap.md`
- Web app/runtime V1 contract: `docs/web-app-v1.md`
- Example catalog: `docs/example-catalog.md`
- Browser harness: `web-runtime/index.html`, `web-runtime/code-vm-web.js`
- Engine wrappers: `lib/engine/*.code`
- Examples: `ConsoleApp1/examples/*.code`
- README: build/run quickstart

## Process Expectations
- When changing semantics: update spec, roadmap, bytecode spec (if opcodes), examples, tests. Run `--run-tests`.
- When making product-direction or workflow decisions: update the relevant docs in the same change. Do not let roadmap, README, and design docs drift.
- Keep examples status-labeled (`runnable`, `negative`, `planned`) and tested so stale draft files do not masquerade as working features.
- Prefer one example per capability cluster rather than one tiny file per minor feature.
- Preserve prior decisions; ask user when unclear.
- Prefer fully spelled-out user-facing names; avoid arbitrary abbreviations unless the term is a widely accepted domain term such as `hud`.

## Change Log
- 2026-04-05: Completed record ergonomics: record methods, inline/external interface implementations, copy-by-value record receivers, structural equality for hashable records, `map`/`set` support for hashable record keys/elements, `NEW_RECORD` bytecode/runtime parity, new examples/tests, and synced docs.
- 2026-04-05: Implemented baseline `record` support with constructor/field syntax, copy-on-assignment/pass/return semantics, deep cloning for nested record and `optional<Record>` fields, and example/test coverage; this was later completed the same day by the full record ergonomics pass above.
- 2026-04-05: Added `switch` statements with `case ... then ...` / optional `default then ...`, no fallthrough, single evaluation of the switch value, constructor definite-assignment support, runnable example coverage, and synced docs/tests.
- 2026-04-05: Added built-in collections `map`, `set`, `queue`, and `stack` to the type checker/code generator/native VM/web VM, added a runnable collections example plus compile/runtime regression coverage, and updated docs/bytecode notes to move collections out of the planned bucket.
- 2026-04-05: Added math/random intrinsics (`minimum`, `maximum`, `absolute`, `sign`, `lerp`, `sine`, `cosine`, `random`) to the native/web host ABI surface, added a runnable example, updated docs, and added target-parity plus web-runtime binding coverage.
- 2026-04-05: Implemented enumerations with strongly typed `Enum.Member` access, explicit integer member values, import/export/re-export support, new enum examples/tests, and updated `shape_dodge.code` to replace obstacle-kind magic integers.
- 2026-04-05: Added `docs/example-catalog.md`, reclassified examples as runnable/negative/planned, added focused examples for implicit `this`, interface-array dispatch, re-export imports, and library artifacts, and updated the harness/docs to align example status with implementation truth.
- 2026-04-05: Fixed web-runtime VM opcode parity for growable arrays by adding browser support for `ARRAY_APPEND` / `ARRAY_REMOVE_AT` and a regression test that compares native VM opcodes against the browser runtime opcode table and switch handlers.
- 2026-04-05: Added `ConsoleApp1/examples/shape_dodge.code` as the canonical small playable web demo, plus harness smoke coverage that compiles and `--build-web`s the repo example without changing engine APIs.
- 2026-04-05: Added namespace imports (`import everything as Name from "path";`), inline interface methods inside object bodies, canonical `engine.viewport` and `engine.scene.SceneLoop` surfaces with compatibility re-export modules, regression tests for the new ergonomics, and updated the web scene example/docs accordingly.
- 2026-04-05: Added typed growable arrays (`append`, `remove_at`), array element type tracking through indexing/foreach/mutation, equality support for compatible reference/value types, explicit-void interface methods, and relaxed interface-call lowering so engine libraries can compile before user implementers are present.
- 2026-04-05: Added `engine.scene` and `engine.loop` with explicit child-object scene composition, split lifecycle interfaces (`Startable`, `Updatable`, `WorldDrawable`, `HudDrawable`), staged registration semantics, and updated tests/example/docs for the new authoring model.
- 2026-04-05: Added implicit `this` lookup in object constructors and methods: unshadowed bare field names resolve to the current object, bare method calls are object-first, constructor definite-field-initialization recognizes implicit field assignment, and the sample scene now uses the shorter style.
- 2026-04-05: Expanded the browser runtime with rectangle outlines, circles, polygons, image/sprite drawing, copied `assets/` output for `--build-web`, and a first higher-level wrapper layer in `lib/engine/` (`engine.colors`, `engine.drawing`, `engine.input`, `engine.view`); added tests and updated the sample scene.
- 2026-04-05: Switched the browser runtime to full-bleed hybrid expansion around a `640x360` safe area, added optional `draw_hud()`, exposed `camera_view_*` / `camera_safe_*` / `screen_width` / `screen_height`, and updated tests/docs/example for the new framing model.
- 2026-04-05: Added the readability naming pass and next primitive layer: canonical `draw_rectangle`, compatibility aliases for legacy `draw_rect` / `std.io.*`, canonical `standard.input_output.*`, compound assignment on variables/fields/array elements, and browser-backed `draw_line` / `draw_text`; updated tests/docs/examples in the same change.
- 2026-04-05: Implemented the first web app/runtime slice: `--build-web`, generated `index.html` + `app.bytecode`, `MainScene` scene metadata extraction, browser-backed full-window scene runtime (`start/update/draw`), and scene intrinsics (`key_down`, `clear`, `draw_rectangle`); added tests and `ConsoleApp1/examples/web_scene.code`.
- 2026-04-05: Repositioned Code as a 2D/web-first language, added `docs/web-app-v1.md`, documented scene-object/full-window/static-site defaults, and formalized the rule that code or product decisions update docs in the same change.
- 2026-02-28: Synced AI context with web-first strategy and backend-agnostic engine API goals; documented capability/fallback policy requirement.
- 2026-02-28: Added near-term platform focus: real browser engine host bindings, engine package wrappers/loop contract, and web bundle workflow.
- 2026-02-13: Added `package` declarations, module-level import/declaration conflict checks, and chained import diagnostics (`a -> b -> c`) for linker errors.
- 2026-02-13: Added module graph tooling (`--dump-module-graph`) and linker tracing (`--trace-linker`) with integration coverage.
- 2026-02-13: Added file-based machine-readable module graph export (JSON/DOT) with `--dump-module-graph <file>` and `--module-graph-format` override.
- 2026-02-14: Implemented modulo operator, enhanced assignments (`+=`/`-=`/`*=` `/=` `%=` + postfix `++/--`), constants (`constant`), void function support (`function<void>` and implicit-void `function name(...)`), and full interpolation expression parsing.
- 2026-02-19: Added compile targets (`--target vm-native|vm-web`) and compile-time capability matrix checks inferred from package/import namespaces; expanded integration tests for target acceptance/rejection.
- 2026-02-19: Added package manifest baseline (`code.package.json`) parser/validator with target compatibility checks, host capability validation, and integration tests.
- 2026-02-19: Added baseline package dependency resolver and lockfile generation (`code.lock.json`) with local package discovery, semver range validation, and lockfile integration tests.
- 2026-02-19: Added `.codelib` library artifact format (read/write/validate), automatic artifact emission for library packages, lockfile preference for validated artifacts, and CLI support to run/disassemble `.codelib` inputs.
- 2026-02-19: Added timing intrinsics (unix/us + monotonic ns/ticks) in type checker/codegen/VM with integration coverage and example program.
- 2026-02-19: Added `HOST_CALL` opcode and native host binding table; migrated compiler lowering for `print` + time intrinsics to host ABI symbols (`standard.input_output.*`, `std.time.*`).
- 2026-02-19: Added host-mode parity scaffold (`vm-native` vs `vm-web`) in VM/CLI and parity test coverage for `print` + time host calls.
- 2026-02-25: Added native-only host ABI intrinsics (`read_line`, `sleep_ms`) with compile-time target gating and web runtime diagnostics; added engine window/input/gfx host stubs and conformance tests.
- 2026-02-25: Added first browser harness in `web-runtime/` (JavaScript bytecode VM + web host binding table for print/time).
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

