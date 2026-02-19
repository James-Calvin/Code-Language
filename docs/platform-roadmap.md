# Platform, Libraries, and Targets Roadmap

Last updated: 2026-02-19

This roadmap is specifically for:
1) library/package system,
2) game-engine development in Code,
3) web deployment target for the VM.

It is intentionally staged so each step de-risks the next one.

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
- Examples: `std.time.now_ms`, `engine.gfx.clear`

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
  - `now_ms() -> integer`
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

### 2.5 Target capability matrix (v1)
- `vm-native`: all capability groups above
- `vm-web`: `std.time`, `std.io.print`, `engine.window`, `engine.input`, `engine.gfx`, `engine.audio`
- compile-time rule: fail build if package requires unsupported capability for selected target.

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
| `[!]` | Phase 1 | Host ABI v1 baseline | runtime host binding table + `HostBindingError` path |
| `[x]` | Phase 2 | Manifest parser + validation | Implemented baseline: nearest-manifest discovery, schema v1 validation, target and host capability checks |
| `[!]` | Phase 2 | Dependency resolver + lockfile | deterministic graph + `code.lock.json` generation |
| `[!]` | Phase 2 | Library artifact format (`.codelib`) | package build/load in linker for both targets |
| `[~]` | Phase 3 | Stdlib as packages | `std.core`, `std.math`, `std.time`, `std.io` packaged and importable |
| `[!]` | Phase 4 | Web VM target runtime | VM in web runtime (WASM or JS host), bytecode loader, browser host bindings |
| `[~]` | Phase 5 | Engine core package set | `engine.math`, `engine.ecs`, `engine.scene`, `engine.loop` |
| `[~]` | Phase 5 | Engine platform adapters | `engine.window/input/gfx/audio` host-backed packages for native+web |
| `[~]` | Phase 6 | Vertical slice game | one small game running on native and web from same Code sources |
| `[_]` | Phase 6 | Registry and remote publishing | package publish/install workflow beyond local workspace |

## 6) Recommended implementation order (next 6 tasks)

1. [x] Add `--target` and target metadata threading through compile/link pipeline.
2. [x] Implement host capability checker (compile-time inference from package/import usage).
3. [x] Implement `code.package.json` parser + validator.
4. Implement local dependency resolver + `code.lock.json`.
5. Add `.codelib` read/write and linker support.
6. Add first host ABI bindings (`std.time.now_ms`, `std.io.print`) for both `vm-native` and `vm-web`.

This order gets library system + target model stable before engine work starts.
