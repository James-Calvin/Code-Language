# AI Context: Draive / Code Language Project

## Purpose
This is the AI-owned, project-life context document for onboarding coding agents.

Use this file as the first read before making changes. It summarizes intent, current decisions, unresolved areas, and collaboration norms.

## Scope
- Project objective: design and implement a programming language named `Code`.
- Current phase: language design/specification with examples; implementation planning follows.
- Canonical language spec: `docs/code-language-spec.md`
- Error design exploration: `docs/exploration.md`

## Quick Start For Agents
1. Read this file fully.
2. Read `docs/code-language-spec.md`.
3. Read `docs/exploration.md` if touching error handling.
4. Treat spec decisions as authoritative unless the user explicitly overrides.
5. When changing semantics, update both the spec and this file in the same task.

## Product Intent (Locked)
- `Code` prioritizes clarity over brevity.
- Syntax should be consistent and intuitive for new developers.
- Learning outcomes should transfer to mainstream languages.
- Object-oriented features are supported.
- No inheritance; interfaces are used for contracts.

## Current Spec Snapshot (Locked Decisions)
- File extension: `.code`.
- Entry point: `main` is optional; used for CLI args.
- Type annotations are required.
- Identifiers: start with letter or `_`; continue with letters/digits/`_`.
- Semicolons required (injection planned later, rules not finalized).
- No `null`; use `optional<T>` with `hasValue`.
- Core numerics: `integer`, `whole`, `real` with sized variants.
- Control flow: `if ... then ...`, `while`, `for`, `foreach`, `break`, `continue`.
- Objects/interfaces:
  - `object`, `interface`, `implement Interface for Object`
  - interface mapping via `method(signature) via Object.method`
  - constructor overloading allowed
- Visibility/modifiers:
  - access: `public`, `package`, `private`
  - default access: `package`
  - `static` and `constant` supported
- Value/reference model:
  - `object` passed by reference
  - `record` object-like but passed by value
  - otherwise follow common C# conventions (provisional)
- Error model:
  - `fallible<T>`
  - hook syntax is fixed as `on error`
  - error shape includes `type`, `message`, `stacktrace`
  - supported patterns include:
    - `on error yield ...`
    - `on error panic(...)`
    - `on error return error`
    - `on error return new error(type, message)`

## Active Open Questions (High Priority)
- Interpolation grammar details.
- Numeric literal grammar (bases, separators, suffixes).
- Exact cast syntax + lossless promotion matrix.
- Overload tie-breaker algorithm.
- Optional unwrapping/narrowing after `hasValue`.
- Module/package lookup details beyond relative `.code` paths.
- `stacktrace` capture semantics and format.

## Collaboration Norms With User
- User drives syntax choices; agent drives question flow and spec clarity.
- Keep documentation dense and implementation-oriented.
- Prefer concrete examples over abstract rules.
- Preserve prior decisions; avoid silent semantic drift.

## Update Protocol (AI-Owned)
- Update this file when any of the following changes:
  - language semantics
  - repo structure relevant to implementation
  - process expectations for future agents
- Keep a short append-only change log below.
- Timestamp entries with absolute date (`YYYY-MM-DD`).

## Change Log
- 2026-02-09: Created AI onboarding context; synchronized with spec v0.8 and current error-hook direction.
