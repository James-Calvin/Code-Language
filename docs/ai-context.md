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
- Core numerics: `integer`, `whole`, `real` with sized variants; numeric literals allow `_` separators, base prefixes `0b/0o/0x`, and sized suffixes `i8/i16/i32/i64`, `w8/w16/w32/w64`, `r16/r32/r64`; unsuffixed map to unsized types; no implicit narrowing.
- Conversions: explicit `as Type`; implicit only for lossless widening within a family and `integer` → `real`; no implicit sign changes or downcasts.
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
    - `on error return <errorExpression>` where the expression type is `error`
  - stacktrace captured on `panic` or unhandled `fallible` as `at function (file:line)` with `type` and `message`
- String interpolation: any expression inside `{ ... }`; escape braces with `\{`/`\}`; nested string literals inside an interpolation are disallowed.
- Optionals: flow narrows inside `if opt.hasValue then`; `opt.value` panics if empty; `opt.or(fallback)` provides default.
- Imports: resolve relative to file, then project `lib/`; `RuntimeLibrary` is stdlib namespace; no global search.
- Overloads: exact match preferred, then fewest/lowest-rank promotions, non-variadic beats variadic; ambiguity is a compile error.
- Precedence: C#-style ordering; unary `+ - not`, `* / %`, `+ -`, relational, equality, `and`, `or`, assignment (right-associative); parentheses override.
- Boolean `or` operator is distinct from the `optional.or(...)` helper method.
- Tooling status (2026-02-10):
  - Bytecode VM with header validation, CALL/RET frames, locals, arithmetic/comparisons, stack ops, jumps, load/store, and PRINT.
  - Compiler prototype (C#) supports var declarations, assignments, arithmetic, comparisons, logical and/or/not (short-circuit), if/then[/else], while, for, foreach (lowered to a 0..n-1 range loop), return, blocks, print statements, function declarations/calls (CALL/RET).
  - CLI: `compile/run/disasm`, token dump, skip-tests; bytecode uses `.bytecode` extension.
  - Roadmap: see docs/features-roadmap.md for status by area.

## Active Open Questions (High Priority)
- Package search beyond project `lib/` (config surface, stdlib layout).

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
- 2026-02-10: Added numeric literal rules (with `w` unsigned suffixes), conversion/promotion rules, interpolation grammar, optionals narrowing/accessors, overload resolution order, import resolution order, stacktrace capture, and error handler return flexibility; bumped spec to v0.9. Added C#-style precedence and removed semicolon-injection from open questions.
- 2026-02-10: VM/bytecode evolved to header v0.3 with CALL/RET; compiler prototype added control flow, logical ops, print statement, functions (decl/call), and CLI utilities (compile/run/disasm/token dump).
