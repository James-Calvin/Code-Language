# Example Catalog

Last updated: 2026-04-05

This catalog is implementation-truthful.

Status legend:
- `runnable`: expected to compile and run or build successfully today
- `negative`: expected to fail with a documented compile-time or runtime diagnostic
- `planned`: illustrative only for a feature that is not implemented yet

Use this document as the primary answer to "does this exist yet?"

## Core Syntax and Control Flow

| Status | Path | Use | Notes |
| --- | --- | --- | --- |
| `runnable` | `ConsoleApp1/examples/arithmetic.code` | `run` | Basic arithmetic and typed locals |
| `runnable` | `ConsoleApp1/examples/forloop.code` | `run` | Counted `for` loop |
| `runnable` | `ConsoleApp1/examples/foreach.code` | `run` | Numeric `foreach` |
| `runnable` | `ConsoleApp1/examples/arrayloop.code` | `run` | Array literals and array iteration |
| `runnable` | `ConsoleApp1/examples/optional.code` | `run` | `optional<T>`, `none`, `.hasValue`, `.value`, `.or(...)` |
| `negative` | `ConsoleApp1/examples/constants.code` | `expected compile error` | Constant reassignment rejection |

## Objects and Interfaces

| Status | Path | Use | Notes |
| --- | --- | --- | --- |
| `runnable` | `ConsoleApp1/examples/object.code` | `run` | Object fields, constructors, and methods |
| `runnable` | `ConsoleApp1/examples/implicit_this.code` | `run` | Implicit field access and bare method calls inside object bodies |
| `runnable` | `ConsoleApp1/examples/interface_dispatch.code` | `run` | Interface dispatch across object values |
| `runnable` | `ConsoleApp1/examples/interface_array_dispatch.code` | `run` | Inline interface methods plus interface-typed arrays |
| `planned` | `ConsoleApp1/examples/record.code` | `planned only` | Draft record syntax sketch; `record` is not implemented yet |

## Modules and Packages

| Status | Path | Use | Notes |
| --- | --- | --- | --- |
| `runnable` | `ConsoleApp1/examples/modules/main.code` | `run` | Basic imports with aliasing |
| `runnable` | `ConsoleApp1/examples/modules/grouped-imports.code` | `run` | Grouped/selective imports |
| `runnable` | `ConsoleApp1/examples/modules/re_exports_main.code` | `run` | Re-export imports |
| `runnable` | `ConsoleApp1/examples/package_manifest_host_requirements/ok/main.code` | `run (--target vm-web)` | Package manifest plus allowed host requirements |
| `negative` | `ConsoleApp1/examples/package_manifest_host_requirements/web_blocked/main.code` | `expected compile error (--target vm-web)` | Manifest host requirement rejected for web target |
| `runnable` | `ConsoleApp1/examples/package_library_artifact/main.code` | `compile-only artifact` | Library package emits `.codelib` and `code.lock.json` |

Current limitation:
- Manifest `targetOverrides.entry` values are parsed and validated, but there is not yet a separate checked-in runnable example because compile entry selection still follows the explicit entry file passed to the compiler.

## Targets, Runtime Diagnostics, and Web Apps

| Status | Path | Use | Notes |
| --- | --- | --- | --- |
| `negative` | `ConsoleApp1/examples/panic.code` | `expected runtime error` | `panic(...)` raises `UserError` with stack info |
| `runnable` | `ConsoleApp1/examples/shape_dodge.code` | `build-web` | Canonical small playable web demo |
| `runnable` | `ConsoleApp1/examples/web_scene.code` | `build-web` | Broader scene-composition and rendering reference |

## Planned But Not Implemented Yet

No checked-in runnable examples exist yet for these planned features:
- enumerations
- `switch`
- user-facing `fallible<T>` / `on error`
- visibility modifiers (`public`, `package`, `private`)
- built-in `map`, `set`, `queue`, `stack`
- standard math/random helpers such as `minimum`, `maximum`, `absolute`, `sign`, `lerp`, `sine`, `cosine`, and `random`
