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
| Numeric literal suffixes such as `i32`, `w64`, `r32` | Draft-only. |
| Exponent numeric literals such as `1e3` or `1.5e-2` | Draft-only. |
| `foreach` over `map`, `set`, `queue`, and `stack` | Deferred. `foreach` supports numeric counts and arrays today; planned map iteration should yield entry values. |

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

For quick prototypes, `fallible<Value>` defaults the error-code type to `integer`, and message-only errors use code `0`:

```code
function<fallible<integer>> load_quick_count() {
  return error("missing count");
}

integer quickCount = load_quick_count() on error {
  print(error.code);
  print(error.message);
  yield 0;
};
```

Do not write planned shorthand yet:

```code
// Planned only. Do not use today.
integer count = try load_count();
```

`break` and `continue` are implemented for loops:

```code
integer i = 0;
while i < 10 then {
  if i == 5 then {
    break;
  }
  if i == 2 then {
    i += 1;
    continue;
  }
  print(i);
  i += 1;
}
```

Output:

```text
0
1
3
4
```

Numeric base prefixes and escaped literal braces in interpolation are also implemented:

```code
print(0b1010);
print(0o17);
print(0x1f);
print("literal \{braces\}");
```

Decimal real literals and limited explicit casts are implemented:

```code
print(1.5);
print(1.);
print(.5);
print(3.8 as integer);
print(3 as real);
```
