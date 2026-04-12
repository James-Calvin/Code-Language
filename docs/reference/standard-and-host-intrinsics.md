# Standard and Host Intrinsics

## Print

`print` writes one value and a newline.

```code
print("hello");
print(2 + 3);
```

Output:

```text
hello
5
```

Notes:

- `print(expr);` is the preferred style.
- The parser also accepts `print expr;`.
- Booleans print as `1` for true and `0` for false.

## Time

| Function | Inputs | Returns | Targets |
| --- | --- | --- | --- |
| `unix_ms()` | none | `integer` Unix milliseconds | native, web |
| `unix_us()` | none | `integer` Unix microseconds | native, web |
| `mono_ns()` | none | `integer` monotonic nanoseconds | native, web |
| `mono_ticks()` | none | `integer` monotonic ticks | native, web |
| `mono_ticks_per_second()` | none | `integer` tick frequency | native, web |
| `sleep_ms(ms)` | `integer` milliseconds | `void` | native only |

Example:

```code
print(unix_ms() > 0);
print(mono_ticks_per_second() > 0);
```

Output:

```text
1
1
```

Common mistakes:

- `sleep_ms` is rejected when compiling for `vm-web`.
- High-range timing values are represented through the current VM numeric model.

## Math and Randomness

| Function | Inputs | Returns |
| --- | --- | --- |
| `minimum(left, right)` | `real`, `real` | `real` |
| `maximum(left, right)` | `real`, `real` | `real` |
| `absolute(value)` | `real` | `real` |
| `sign(value)` | `real` | `integer` |
| `lerp(start, end, amount)` | `real`, `real`, `real` | `real` |
| `sine(angle)` | `real` | `real` |
| `cosine(angle)` | `real` | `real` |
| `random()` | none | `real` in `[0, 1)` |

Example:

```code
print(minimum(4, 9));
print(maximum(4, 9));
print(absolute(-3));
print(sign(-3));
print(lerp(10, 20, 1 / 4));
print(cosine(0));
```

Output:

```text
4
9
3
-1
12.5
1
```

Common mistakes:

- Angles for `sine` and `cosine` use the runtime math convention, not degrees.
- `random()` returns a real value from 0 inclusive to 1 exclusive.

## Native Input

| Function | Inputs | Returns | Targets |
| --- | --- | --- | --- |
| `read_line()` | none | `string` | native only |

Example:

```code
string line = read_line();
print("you typed: {line}");
```

Common mistakes:

- `read_line` is rejected when compiling for `vm-web`.
- Browser input should use the web scene input APIs instead.

## Prototype Engine Host Intrinsics

These lower-level host intrinsics exist, but scene apps should prefer [Web Apps and Engine Modules](web-apps-and-engine.md).

| Function | Inputs | Returns |
| --- | --- | --- |
| `window_create(title, width, height)` | `string`, `integer`, `integer` | `whole` |
| `window_should_close(window)` | `whole` | `boolean` |
| `window_present(window)` | `whole` | `void` |
| `input_key_down(window, keycode)` | `whole`, `integer` | `boolean` |
| `gfx_clear(window, r, g, b, a)` | `whole`, `real`, `real`, `real`, `real` | `void` |
| `gfx_draw_rect(window, x, y, w, h, r, g, b, a)` | `whole`, eight `real` values | `void` |

Example:

```code
whole window = window_create("demo", 320, 200);
print(window > 0);
print(window_should_close(window));
```

Behavior:

- These calls are prototype host bindings.
- Current native and web VM host tables keep them available for parity and bring-up.
- The default browser app workflow uses `--build-web`, `MainScene`, and the scene runtime instead.

## Target Restrictions

Compile target is selected with:

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --target vm-web --compile-only ConsoleApp1/examples/time.code
```

Native-only calls rejected on `vm-web`:

- `read_line()`
- `sleep_ms(ms)`

Capability checks also consider package/import namespaces and manifest `hostAbi.requires`.
