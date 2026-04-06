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
| `runnable` | `ConsoleApp1/examples/enum.code` | `run` | Strongly typed enumerations with `Enum.Member` access |
| `runnable` | `ConsoleApp1/examples/record.code` | `run` | Copy-by-value records with methods, interface conformance, and return-and-reassign value updates |
| `runnable` | `ConsoleApp1/examples/switch.code` | `run` | `switch` statements with enum and integer cases |
| `runnable` | `ConsoleApp1/examples/forloop.code` | `run` | Counted `for` loop |
| `runnable` | `ConsoleApp1/examples/foreach.code` | `run` | Numeric `foreach` |
| `runnable` | `ConsoleApp1/examples/arrayloop.code` | `run` | Array literals and array iteration |
| `runnable` | `ConsoleApp1/examples/optional.code` | `run` | `optional<T>`, `none`, `.hasValue`, `.value`, `.or(...)` |
| `negative` | `ConsoleApp1/examples/constants.code` | `expected compile error` | Constant reassignment rejection |

## Stdlib and Runtime Helpers

| Status | Path | Use | Notes |
| --- | --- | --- | --- |
| `runnable` | `ConsoleApp1/examples/time.code` | `run` | Time intrinsics |
| `runnable` | `ConsoleApp1/examples/math_random.code` | `run` | Math and randomness helpers |
| `runnable` | `ConsoleApp1/examples/collections.code` | `run` | Built-in `map`, `set`, `queue`, `stack`, shared `.length`, and map indexing |

## Objects and Interfaces

| Status | Path | Use | Notes |
| --- | --- | --- | --- |
| `runnable` | `ConsoleApp1/examples/object.code` | `run` | Object fields, constructors, and methods |
| `runnable` | `ConsoleApp1/examples/implicit_this.code` | `run` | Implicit field access and bare method calls inside object bodies |
| `runnable` | `ConsoleApp1/examples/interface_dispatch.code` | `run` | Interface dispatch across object values |
| `runnable` | `ConsoleApp1/examples/interface_array_dispatch.code` | `run` | Inline interface methods plus interface-typed arrays |

## Modules and Packages

| Status | Path | Use | Notes |
| --- | --- | --- | --- |
| `runnable` | `ConsoleApp1/examples/modules/main.code` | `run` | Basic imports with aliasing |
| `runnable` | `ConsoleApp1/examples/modules/grouped-imports.code` | `run` | Grouped/selective imports |
| `runnable` | `ConsoleApp1/examples/modules/re_exports_main.code` | `run` | Re-export imports |
| `runnable` | `ConsoleApp1/examples/modules/visibility_main.code` | `run` | Top-level `public` / `package` / `private` visibility with package-aware imports |
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
- user-facing `fallible<T>` / `on error`
- member-level visibility for fields and methods

Record notes:
- Record methods are value-helper methods: `this` is cloned at method entry.
- Hashable records support structural equality and may be used as `map` keys or `set` elements.
- Records with non-hashable fields still work as data types, but equality and key/set usage are rejected at compile time.
