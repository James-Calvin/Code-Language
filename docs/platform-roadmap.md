# Platform, Libraries, and Targets Roadmap

Last updated: 2026-02-28

This roadmap is specifically for:
1) library/package system,
2) game-engine development in Code,
3) web deployment target for the VM.

It is intentionally staged so each step de-risks the next one.

## 0) Strategic intent (web-first, platform-agnostic)

Primary product direction:
- First-class support for web-hosted workloads (simulation, ML experiments, computationally heavy apps).
- Keep Code source portable across `vm-web` and `vm-native`.

Architecture rule:
- Language-facing APIs stay backend-agnostic (for example `engine.gfx`, later `engine.gpu`).
- Runtime host bindings map those APIs to concrete platform backends (WebGPU/WebGL/Canvas on web, native GPU stacks on desktop).
- Capability discovery + explicit fallback policy is required so behavior is predictable, not implicit magic.

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

## 2) Host ABI v1 (concrete draft)

Host calls are explicit, capability-scoped bindings provided by the runtime host.

### 2.1 ABI symbol naming
- Fully qualified symbol: `<namespace>.<member>`
- Examples: `std.time.unix_ms`, `engine.gfx.clear`

### 2.2 Value model
- ABI uses existing VM value domain:
  - numbers (`integer`/`whole`/`real` at type-check level, numeric value at VM level)
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
- `std.io`
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
  - `draw_rect(whole window, real x, real y, real w, real h, real r, real g, real b, real a) -> void`
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
- `vm-web`: `std.time`, `std.io.print`, `engine.window`, `engine.input`, `engine.gfx`, `engine.audio`
- compile-time rule: fail build if package requires unsupported capability for selected target.

### 2.6 Backend policy (WebGPU compatibility)
- Current JS web runtime does **not** block a WebGPU future; it is a bootstrap runtime for ABI bring-up.
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
- `vm-web`: `.bytecode` + JS/WASM loader bundle + minimal HTML bootstrap

## 5) Streamlined execution roadmap

Legend:
- `[!]` high priority
- `[~]` medium priority
- `[_]` low priority
- `[x]` complete

| Priority | Phase | Work Item | Deliverable / Exit Criteria |
| --- | --- | --- | --- |
| `[x]` | Phase 1 | Target flag + capability validation | Implemented: `--target vm-native|vm-web` with compile-time capability matrix checks |
| `[~]` | Phase 1 | Host ABI v1 baseline | Implemented baseline `HOST_CALL` + native/web host tables + `HostBindingError`; includes `std.io.print`, `std.time.*`, native-only `read_line`/`sleep_ms` diagnostics, and engine window/input/gfx no-op stubs |
| `[x]` | Phase 2 | Manifest parser + validation | Implemented baseline: nearest-manifest discovery, schema v1 validation, target and host capability checks |
| `[x]` | Phase 2 | Dependency resolver + lockfile | Implemented baseline local resolver + deterministic `code.lock.json` generation (target-scoped) |
| `[x]` | Phase 2 | Library artifact format (`.codelib`) | Implemented baseline: library manifests emit `.codelib`, resolver validates/prefer artifact paths in `code.lock.json`, CLI can run/disasm `.codelib` |
| `[~]` | Phase 3 | Stdlib as packages | `std.core`, `std.math`, `std.time`, `std.io` packaged and importable |
| `[!]` | Phase 4 | Web VM target runtime | Browser runtime preview is in place (`web-runtime/` JS bytecode harness + web host bindings); continue toward production WASM/JS target packaging and runtime parity |
| `[!]` | Phase 4 | Web engine host bindings (real impl) | Replace window/input/gfx no-op web stubs with concrete browser bindings and conformance tests |
| `[!]` | Phase 4 | Backend-agnostic API contract | Freeze capability-query and fallback semantics so one Code source can target multiple backends predictably |
| `[~]` | Phase 5 | Engine core package set | `engine.math`, `engine.ecs`, `engine.scene`, `engine.loop` |
| `[~]` | Phase 5 | Engine platform adapters | `engine.window/input/gfx/audio` host-backed packages for native+web |
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
6. [x] Add first host ABI bindings (`std.io.print`, `std.time.*`) for both `vm-native` and `vm-web`, then extend native-only APIs (`read_line`, `sleep_ms`) with target diagnostics.

This order gets library system + target model stable before engine work starts.

## 7) Near-term execution milestones (web-first)

1. **Real web engine host bindings**
   - Implement browser-backed `engine.window`, `engine.input`, and `engine.gfx` handlers.
   - Exit criteria: a Code sample renders and responds to keyboard input in browser.

2. **Engine packages + loop contract**
   - Add importable `engine.window`, `engine.input`, `engine.gfx`, `engine.loop` packages over host ABI.
   - Exit criteria: same source compiles/runs on both targets with documented behavior.

3. **Web bundle workflow**
   - Add a CLI web bundle mode that outputs bytecode + loader + HTML scaffold.
   - Exit criteria: one command produces a runnable browser folder for a sample app.
