# `fallible<T>` Ergonomics Exploration

## Goal
Compare syntax strategies for handling errors as data while preserving Code's priorities:
- clarity over brevity
- predictable behavior for new developers
- explicit control at error boundaries

## Baseline (Current Spec)
Current behavior is explicit at call sites:

```code
real value = parseReal(text) on error yield 0;
real ratio = divide(value, count) on error panic("Division failed: {error}");
fallible<real> pending = parseReal(text);
```

This is clear and beginner-friendly, but verbose in multi-step workflows.

## Strategy A: Keep Baseline Only
No new syntax; use only `on error`.

Example (parse -> compute):

```code
real parsed = parseReal(input) on error yield 0;
real output = compute(parsed) on error yield 0;
print(output);
```

Common use-cases:
- CLI utilities with obvious fallback values.
- Teaching explicit handling at every risky call.

Tradeoffs:
- Most explicit.
- Repetitive in deep call chains.

## Strategy B: Add Propagation Operator (`?`)
Allow fast propagation when the current function returns `fallible<T>`.

Proposed rule:
- `expr?` unwraps success value.
- On failure, returns error from current function immediately.

Example (parse -> compute):

```code
function<fallible<real>> run(string input, real count) {
  real parsed = parseReal(input)?;
  real output = divide(parsed, count)?;
  return output;
}
```

Call-site boundary stays explicit:

```code
real result = run(text, 2) on error yield 0;
```

Common use-cases:
- Service/business logic with several fallible steps.
- Library code that propagates errors upward.

Tradeoffs:
- Large readability gain in pipelines.
- Introduces one compact operator to teach.

## Strategy C: Add Combinators (`map`, `then`, `mapError`)
Keep data-flow functional and chainable.

Possible API shape:
- `map` transforms success value.
- `then` chains to next fallible operation.
- `mapError` rewrites error payload.

Example (parse -> compute):

```code
fallible<real> result =
  parseReal(input)
    .then(function<fallible<real>>(real v) { return divide(v, count); })
    .map(function<real>(real q) { return q * 100; });

real output = result on error yield 0;
```

Common use-cases:
- Reusable error pipelines.
- Rich error decoration before boundary handling.

Tradeoffs:
- Powerful and composable.
- Higher conceptual cost for beginners.

## Strategy D: Structured Match (`when success` / `when error`)
Add pattern-style branching for fallible values.

Example:

```code
fallible<real> parsed = parseReal(input);
real output = when parsed {
  success(real value) then value;
  error(e) then {
    print("Parse failed: {e}");
    0;
  }
};
```

Common use-cases:
- UI/controller layers where success/error paths are both substantial.
- Cases where both branches need clear local logic.

Tradeoffs:
- Very explicit branch semantics.
- More syntax surface than `on error`.

## Recommendation
Best balance for Code: **Strategy B (Propagation `?`) + current `on error` boundary syntax**.

Why:
- Keeps top-level handling explicit (`on error ...`).
- Reduces noise in internal logic.
- Easy mental model: `?` means "return error now if failed."

## Suggested Minimal Spec Additions (if Strategy B chosen)
1. `expr?` allowed only inside functions returning `fallible<U>`.
2. `expr?` requires `expr` type `fallible<T>` and evaluates to `T`.
3. On error, function returns that error immediately.
4. `on error` remains the required explicit conversion from `fallible<T>` to `T`.
5. `yield` inside `fallible<T>` functions returns a success value of `T`.

## Decision Prompts
1. Do you want to add `?` now, or keep baseline-only first?
2. If `?` is added, should it work in constructors/methods the same as functions?
3. Do you want combinators (`map`, `then`) in v1, or defer to runtime libraries later?
