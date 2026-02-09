# Error Hooks Exploration

## Goal
Design the cleanest possible error-as-data flow for Code without adding shorthand operators prematurely.

Decision status:
- Hook keyword is currently fixed as `on error`.

Constraints:
- Keep behavior explicit for beginners.
- Keep propagation readable in multi-step workflows.
- Keep one consistent model across functions, methods, and modules.

## Current Baseline
Current draft behavior already supports:

```code
real parsed = parseReal(input) on error yield 0;
real value = parseReal(input) on error panic("Could not parse: {error}");
fallible<real> pending = parseReal(input);
```

And now propagation from inside a `fallible<T>` function:

```code
function<fallible<real>> run(string input, real count) {
  real parsed = parseReal(input) on error return error;
  return divide(parsed, count);
}
```

Error transformation is also supported:

```code
function<fallible<real>> run2(string input, real count) {
  real parsed = parseReal(input)
    on error return new error("ParseError", "Could not parse '{input}'");
  return divide(parsed, count);
}
```

## What High-Quality Languages Do Well
Notable patterns from languages commonly praised for error handling:

- Rust:
  - Typed `Result<T, E>`.
  - `?` operator for early return on error.
  - Strong compiler checks make flows explicit.
- Go:
  - Explicit `error` return values.
  - Straightforward `if err != nil` handling.
  - Very readable, but can be repetitive.
- Zig:
  - Error unions (`T!E`) and `try/catch`.
  - Explicit recovery (`catch`) or propagation (`try`) with low ceremony.
- Elixir:
  - Tagged tuples (`{:ok, value}`, `{:error, reason}`).
  - Pattern matching drives explicit control flow.
  - Consistent "errors are values" discipline.
- OCaml:
  - `result` type plus composition helpers (`map`, `bind`).
  - Clean typed pipelines for transformation-heavy code.

Shared themes:
- Errors are typed data.
- Propagation is easy.
- Boundary handling stays explicit.

## Hook Syntax Candidates (No Shorthand Operator)
All candidates preserve your new valid pattern concept: explicit propagation from hook site.

### Candidate A: Keep `on error` (minimal change)
```code
real parsed = parseReal(input) on error return error;
real output = divide(parsed, count) on error yield 0;
```

Pros:
- Already in the spec.
- Reads naturally.
- Minimal grammar churn.

Cons:
- Slightly phrase-heavy when repeated.

### Candidate B: `catch error` (familiar to many developers)
```code
real parsed = parseReal(input) catch error return error;
real output = divide(parsed, count) catch error yield 0;
```

Pros:
- Familiar from many ecosystems.
- Explicit error hook noun (`error`) stays visible.

Cons:
- Can be confused with statement-level `try/catch`.

### Candidate C: `handle error` (most explicit English)
```code
real parsed = parseReal(input) handle error return error;
real output = divide(parsed, count) handle error yield 0;
```

Pros:
- Very clear for beginners.
- Reads like an intent statement.

Cons:
- Longer syntax.

### Candidate D: Hook block form for complex handlers
Keep a single keyword but make block form first-class:

```code
real parsed = parseReal(input) on error {
  log("Parse failed: {error}");
  return error;
};
```

Pros:
- Scales to richer recovery logic.
- Maintains one concept (`on error`) in all forms.

Cons:
- Requires explicit terminal action rules (`yield`, `return error`, or `panic`).

## Suggested Direction
Most aligned with Code's clarity goal:
- Keep `on error` for now.
- Expand legal terminal actions in handler blocks to:
  - `yield <value>`
  - `panic(...)`
  - `return error` (inside `fallible<T>` functions)

This gives strong semantics now without adding shorthand operators.

## Open Design Checks
1. Should block handlers support `return <newError>` for explicit transformation?
2. Should `error` have a concrete base type in the language spec?
3. Should any expression support hooks, or only `fallible<T>` expressions?
4. Should handler blocks be required to end in one terminal action?

## Next Prompt
Should block handlers support explicit transformed returns too?
1. `on error return new error("Type", "Message")` only
2. Generic `on error return <errorExpression>`
