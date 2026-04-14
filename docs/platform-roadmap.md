# Platform, Libraries, and Targets Roadmap

Last updated: 2026-04-14

This roadmap is specifically for:
1) library/package system,
2) game-engine development in Code,
3) web deployment target for the VM.

It is intentionally staged so each step de-risks the next one.

## 0) Strategic intent (web-first, platform-agnostic)

Primary product direction:
- First-class support for code-first 2D interactive applications and games deployed on the web.
- Keep Code source portable across `vm-web` and `vm-native`.
- The default user story should be: write Code, build once, and receive a deployable website.

Architecture rule:
- Language-facing APIs stay backend-agnostic (for example `engine.gfx`, later `engine.gpu`).
- Runtime host bindings map those APIs to concrete platform backends (WebGPU/WebGL/Canvas on web, native GPU stacks on desktop).
- Capability discovery + explicit fallback policy is required so behavior is predictable, not implicit magic.

Product-default rule:
- The browser runtime, not raw window management, should own the initial app shell.
- Web apps should fill the browser window by default.
- The V1 runtime contract is defined in `docs/web-app-v1.md`.

## 1) Build Model (target interaction)

Single source pipeline:
- `.code` source -> parser/type checker -> target-independent bytecode IR
- target-specific linker/bundler -> runtime package

Design rule:
- Language semantics stay target-neutral.
- Target selection only changes host bindings and packaging/output shape.

Target IDs (v1):
- `vm-native`
- `vm-web`

CLI direction:
- `--target vm-native|vm-web`
- default: `vm-native` when omitted.

Near-term web build goal:
- A dedicated web build mode now emits a deployable static site folder via `--build-web`.
- Default output directory: `dist/` in the package root when a manifest exists, otherwise `dist/` beside the entry `.code` file.
- Current output: `index.html` with embedded bytecode so the page opens directly, plus copied `assets/` content when present.
- Optional output: pass `--emit-web-bytecode` with `--build-web` to also write `app.bytecode` for debugging or inspection.
- Current recommended authoring model: keep `MainScene` thin and compose child objects through `engine.scene.Scene` and `engine.scene.SceneLoop` (with `engine.loop` retained as a compatibility re-export during migration).
- Planned authoring model: add a target-agnostic graphical app profile that supports top-level lifecycle authoring and an implicit engine prelude, synthesizes the web entry shell first, and leaves explicit `MainScene` valid.
- The current `web-runtime/index.html` upload flow remains preview-only bootstrap tooling for raw bytecode bring-up and is no longer the primary workflow.

## 2) Host ABI v1 (concrete draft)

Host calls are explicit, capability-scoped bindings provided by the runtime host.

### 2.1 ABI symbol naming
- Fully qualified symbol: `<namespace>.<member>`
- Examples: `std.time.unix_ms`, `engine.gfx.clear`

### 2.2 Value model
- ABI uses existing VM value domain:
  - numbers (`integer`/`whole`/`real` plus sized numeric boundary types at type-check level, numeric value at VM level)
  - `boolean`
  - `string`
  - arrays/objects/handles (boxed runtime values)
- v1 return convention: one return slot.
  - `void` host functions return `0` (caller discards in statement position).

### 2.3 Call convention
- New runtime boundary call form (compiler-level concept): `hostcall(symbol, args...)`
- Runtime dispatch:
  1) resolve symbol from host table,
  2) validate arity at runtime,
  3) execute host function,
  4) push one return value.
- Runtime error type for missing binding: `HostBindingError`.

### 2.4 Capability groups
- `std.time`
  - `unix_ms() -> integer`
  - `unix_us() -> integer`
  - `mono_ns() -> integer`
  - `mono_ticks() -> integer`
  - `mono_ticks_per_second() -> integer`
  - `sleep_ms(integer ms) -> void` (`vm-native` only in v1)
- `standard.input_output`
  - `print(string value) -> void`
  - `read_line() -> string` (`vm-native` only in v1)
- `std.fs` (`vm-native` only in v1)
  - `read_text(string path) -> string`
  - `write_text(string path, string value) -> void`
- `engine.window`
  - `create(string title, integer width, integer height) -> whole` (window handle)
  - `should_close(whole window) -> boolean`
  - `present(whole window) -> void`
- `engine.input`
  - `key_down(whole window, integer keycode) -> boolean`
- `engine.gfx`
  - `clear(whole window, real r, real g, real b, real a) -> void`
  - `draw_rectangle(whole window, real x, real y, real w, real h, real r, real g, real b, real a) -> void`
- `engine.audio`
  - `play_sfx(string id, real volume) -> void`
- `engine.gpu` (planned)
  - adapter/device discovery, capability query, and limits
  - buffer/texture creation and updates
  - render/compute pipeline creation
  - command encoding + submission
  - compute dispatch and GPU timing/query hooks

### 2.5 Target capability matrix (v1)
- `vm-native`: all capability groups above
- `vm-web`: `std.time`, `standard.input_output.print`, `engine.window`, `engine.input`, `engine.gfx`, `engine.audio`
- compile-time rule: fail build if package requires unsupported capability for selected target.

### 2.6 Backend policy (WebGPU compatibility)
- Current JS web runtime does **not** block a WebGPU future; it is a bootstrap runtime for ABI bring-up.
- Current JS web runtime also does **not** block a future Wasm VM/runtime path; keep JS as the default until measured performance, native/web VM parity, or startup-size data justifies the added toolchain complexity.
- The long-term design keeps `engine.*` APIs backend-agnostic and maps them per target/backend.
- Web target backend preference:
  1) WebGPU when available,
  2) fallback backend (for example WebGL2/Canvas) when policy allows,
  3) deterministic diagnostic when required capability is unavailable.
- Native target uses the same ABI contract with a native backend implementation for parity.

## 3) Package Manifest Schema (concrete draft)

Manifest file: `code.package.json`

```json
{
  "schemaVersion": 1,
  "name": "engine.core",
  "version": "0.1.0",
  "kind": "library",
  "entry": "src/main.code",
  "exports": {
    "ecs": "src/ecs.code",
    "math": "src/math.code"
  },
  "targets": ["vm-native", "vm-web"],
  "dependencies": {
    "std.core": "^0.1.0"
  },
  "devDependencies": {
    "test.assert": "^0.1.0"
  },
  "targetOverrides": {
    "vm-web": {
      "entry": "src/main_web.code"
    }
  },
  "hostAbi": {
    "requires": ["engine.window", "engine.input", "engine.gfx"]
  }
}
```

Field rules:
- `schemaVersion`: integer, required.
- `name`: package identifier, required.
- `version`: semver string, required.
- `kind`: `library` or `application`.
- `entry`: module entry path for package build.
- `exports`: map of import-facing export name -> file path.
- `targets`: supported target IDs.
- `dependencies`/`devDependencies`: package -> semver range.
- `targetOverrides`: target-specific manifest overrides.
- `hostAbi.requires`: required capability groups.

### 3.1 Lockfile schema

Lockfile file: `code.lock.json`

```json
{
  "schemaVersion": 1,
  "target": "vm-web",
  "packages": [
    {
      "name": "engine.core",
      "version": "0.1.0",
      "resolved": "./packages/engine.core-0.1.0.codelib",
      "integrity": "sha256-..."
    }
  ]
}
```

Lockfile role:
- freezes dependency graph per target,
- stores resolved artifact path + integrity,
- used by linker for reproducible builds.

## 4) Artifact strategy

Library artifact:
- extension: `.codelib`
- contains:
  - bytecode module bundle,
  - export table,
  - manifest snapshot,
  - target metadata.

Application artifact:
- `vm-native`: `.bytecode` + optional runner metadata
- `vm-web`: static site output; current implementation emits `index.html` with an inlined JS loader/runtime and embedded bytecode. `app.bytecode` is emitted only when `--emit-web-bytecode` is passed for debugging or inspection.

## 5) Streamlined execution roadmap

Legend:
- `[!]` high priority
- `[~]` medium priority
- `[_]` low priority
- `[x]` complete

| Priority | Phase | Work Item | Deliverable / Exit Criteria |
| --- | --- | --- | --- |
| `[x]` | Phase 1 | Target flag + capability validation | Implemented: `--target vm-native|vm-web` with compile-time capability matrix checks |
| `[~]` | Phase 1 | Host ABI v1 baseline | Implemented baseline `HOST_CALL` + native/web host tables + `HostBindingError`; includes `standard.input_output.print`, `std.time.*`, native-only `read_line`/`sleep_ms` diagnostics, and engine window/input/gfx no-op stubs |
| `[x]` | Phase 2 | Manifest parser + validation | Implemented baseline: nearest-manifest discovery, schema v1 validation, target and host capability checks |
| `[x]` | Phase 2 | Dependency resolver + lockfile | Implemented baseline local resolver + deterministic `code.lock.json` generation (target-scoped) |
| `[x]` | Phase 2 | Library artifact format (`.codelib`) | Implemented baseline: library manifests emit `.codelib`, resolver validates/prefer artifact paths in `code.lock.json`, CLI can run/disasm `.codelib` |
| `[x]` | Phase 3 | Web app/runtime V1 contract | Documented in `docs/web-app-v1.md`: scene-object authoring, `start/update/draw` plus optional `draw_hud`, full-window browser runtime, centered `640x360` safe area, hybrid-expanded framing, and static-site output target |
| `[~]` | Phase 3 | Stdlib as packages | `std.core`, `std.math`, `std.time`, `standard.input_output` packaged and importable |
| `[x]` | Phase 4 | Web bundle workflow | Implemented: `--build-web` emits a runnable static site folder with `index.html`, embedded bytecode, copied `assets` output when present, and optional `app.bytecode` via `--emit-web-bytecode` instead of relying on the preview harness |
| `[x]` | Phase 4 | Web build artifact polish | Implemented: generated apps default to embedded bytecode in `index.html` without writing duplicate `app.bytecode`; `--emit-web-bytecode` writes the separate bytecode artifact when needed |
| `[~]` | Phase 4 | Browser-backed web app runtime | Implemented current slice: generated full-window canvas runtime, `MainScene` lifecycle (`start/update/draw` plus optional `draw_hud`), fixed-step loop, centered `640x360` safe area, hybrid-expanded world framing, copied `assets` output, browser-backed rectangles/outlines/lines/circles/polygons/text/images/sprites, app-key scroll prevention, and console-routed web `print`; expand richer input/audio/content handling |
| `[~]` | Phase 4 | Web engine host bindings (real impl) | Implemented current scene-runtime bindings for `key_down`, `clear`, `draw_rectangle`, `draw_rectangle_outline`, `draw_line`, `draw_circle`, `draw_circle_outline`, `draw_polygon`, `draw_polygon_outline`, `draw_text`, `draw_image`, `draw_sprite`, `camera_view_*`, `camera_safe_*`, and `screen_width` / `screen_height`; legacy window-handle web stubs still need real implementations or package-level wrappers |
| `[!]` | Phase 4 | Backend-agnostic API contract | Freeze capability-query and fallback semantics so one Code source can target multiple backends predictably |
| `[!]` | Phase 5 | Target-agnostic graphical app profile | Allow top-level `start`/`update`/`draw`/`draw_hud` authoring with an implicit engine prelude; synthesize the web entry shell first while preserving explicit `MainScene`, and keep the design portable to future native graphical targets |
| `[~]` | Phase 5 | Engine core package set | Canonical `engine.scene` now exports `Scene`, `SceneLoop`, and lifecycle interfaces for explicit child-object composition; broader engine packages such as `engine.math` and `engine.ecs` are still pending |
| `[~]` | Phase 5 | Engine platform adapters | Wrapper layer now includes canonical `engine.colors`, `engine.drawing`, `engine.input`, `engine.viewport`, and `engine.scene` plus compatibility re-export modules `engine.view` / `engine.loop`; still need fuller host-backed package taxonomy for native+web, byte-channel color overloads on top of the implemented `byte` / `whole8` surface, plus audio |
| `[~]` | Phase 5 | `engine.gpu` ABI v1 | Add GPU resource/pipeline/dispatch ABI for compute-heavy and graphics-heavy workloads |
| `[~]` | Phase 5 | WebGPU backend | Implement `engine.gpu` on `vm-web` with explicit fallback policy when WebGPU is unavailable |
| `[~]` | Phase 5 | Native GPU backend parity | Implement the same `engine.gpu` ABI on `vm-native` backend(s) for parity and performance |
| `[~]` | Phase 6 | Vertical slice game | one small game running on native and web from same Code sources |
| `[_]` | Phase 6 | Registry and remote publishing | package publish/install workflow beyond local workspace |

## 6) Recommended implementation order (next 6 tasks)

1. [x] Add `--target` and target metadata threading through compile/link pipeline.
2. [x] Implement host capability checker (compile-time inference from package/import usage).
3. [x] Implement `code.package.json` parser + validator.
4. [x] Implement local dependency resolver + `code.lock.json`.
5. [x] Add `.codelib` read/write and linker support.
6. [x] Add first host ABI bindings (`standard.input_output.print`, `std.time.*`) for both `vm-native` and `vm-web`, then extend native-only APIs (`read_line`, `sleep_ms`) with target diagnostics.

This order gets library system + target model stable before engine work starts.

## 7) Near-term execution milestones (web-first)

1. **Freeze and keep the V1 contract**
   - Treat `docs/web-app-v1.md` as the implementation contract for the first end-to-end browser app workflow.
   - Exit criteria: docs, roadmap, and README all point to the same scene-object/full-window/static-site direction with no contradictory claims.

2. **Web bundle workflow**
   - Implemented first slice: `--build-web` emits a runnable static site folder instead of a raw `.bytecode` file plus a manual upload step.
   - Current state: one command produces a runnable browser folder for a sample app, defaulting to `dist/`.

3. **Browser-backed app runtime**
   - Implemented current slice: generated app page and browser runtime fill the window, preserve aspect ratio with a centered `640x360` safe area, expand the visible world when needed, support optional `draw_hud`, own the main loop for a `MainScene`, and expose rectangles/outlines/lines/circles/polygons/text/images/sprites.
   - Next step: expand richer input/audio/content handling while keeping the higher-level engine-facing API off raw window handles.

4. **Engine packages + loop contract**
   - Implemented current slice: importable wrapper modules now exist for colors, drawing, input, viewport queries, scene composition, and scene-loop execution under `lib/engine/`.
   - Next step: grow that wrapper layer into broader `engine.window`, `engine.input`, `engine.gfx`, audio, and higher-level content helpers without collapsing back to raw host symbols.
   - Exit criteria: the runtime contract is reflected in engine-facing modules rather than only raw host ABI symbols.

5. **Graphical app profile**
   - Planned notes-derived direction: reduce `MainScene` and import boilerplate with a target-agnostic app profile, top-level lifecycle authoring, and an implicit engine prelude.
   - First implementation target should be `--build-web`; explicit `MainScene` remains supported as the compatibility and advanced path.

