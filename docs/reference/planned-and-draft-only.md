# Planned and Draft-Only Features

This page lists features mentioned in design docs or roadmap notes that should not be taught as implemented language behavior yet.

Use [Example Catalog](../example-catalog.md) and the current compiler as the truth source for runnable examples.

## Language Features

| Feature | Status |
| --- | --- |
| Fallible propagation shorthand, such as `try` | Planned. Use explicit `on error` handlers today. |
| `fallible<void, E>` | Deferred. Fallible success type cannot be `void` today. |
| Semicolon injection | Draft-only. Write semicolons today. |
| Sized numeric types such as `integer8`, `whole32`, `real64` | Draft-only in current docs. |
| Numeric base prefixes such as `0b`, `0o`, `0x` | Draft-only. Use decimal integer literals today. |
| Numeric literal suffixes such as `i32`, `w64`, `r32` | Draft-only. |
| Decimal point real literals such as `1.5` | Not implemented in the lexer today. Use operations or functions that produce `real`. |
| User-written casts such as `value as Type` | Draft-only. There is no parser support today. |
| `break` and `continue` | Mentioned in some docs, but not accepted by the current parser. Use conditional control flow today. |
| `foreach` over `map`, `set`, `queue`, and `stack` | Deferred. `foreach` supports numeric counts and arrays today. |
| Escaped literal braces in interpolated strings | Draft-only. Use concatenation for literal braces today. |

## Package and Module Features

| Feature | Status |
| --- | --- |
| Automatic compile entry selection from `targetOverrides.entry` | Parsed and validated, but compile entry still follows the explicit CLI input file. |
| Broader package namespace enforcement | Deferred. |
| Remote package registry or publishing | Planned. Current dependency resolution is local. |
| Stable stdlib layout and versioning | Planned. Current built-ins and `lib/engine` wrappers are the active surface. |

## Runtime and Engine Features

| Feature | Status |
| --- | --- |
| Broader standard library beyond current containers, math, time, IO baseline | Planned. |
| Mouse/touch input | Out of current V1 web slice. |
| Audio APIs | Out of current V1 web slice. |
| Physics APIs | Out of current V1 web slice. |
| Richer content handling and asset pipeline | Planned. Current web build copies `assets/`. |
| Real browser-backed implementations for remaining raw window-handle engine stubs | Planned or wrapper-directed. Scene-runtime drawing/input are the current default. |
| Capability query and fallback APIs | Planned design requirement. |
| `engine.gpu`, WebGPU backend, native GPU parity | Roadmap item. |
| Wasm-hosted VM/runtime | Deferred until performance, parity, or startup-size data justifies it. |

## Current Recommendation

Use the implemented shape:

```code
enum LoadError {
  Missing;
}

function<fallible<integer, LoadError>> load_count() {
  return error(LoadError.Missing);
}

integer count = load_count() on error {
  yield 0;
};
```

Do not write planned shorthand yet:

```code
// Planned only. Do not use today.
integer count = try load_count();
```

Use conditional loops instead of `break` or `continue`:

```code
integer i = 0;
boolean running = true;
while i < 10 and running then {
  if i == 5 then {
    running = false;
  } else {
    print(i);
  }
  i += 1;
}
```

Output:

```text
0
1
2
3
4
```
