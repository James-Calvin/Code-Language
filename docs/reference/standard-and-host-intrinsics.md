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
print(lerp(10, 20, 1. / 4));
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
- Integral `/` is truncating integer division. Use a `real` operand for real division, for example `1. / 4`.

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
- The default browser app workflow uses `compiler entry.code`, either an explicit `MainScene` or an inferred top-level lifecycle entry, and the scene runtime instead.

## Generated Web Scene Input Intrinsics

Generated web apps expose a browser-backed scene input surface. Native execution and web execution without an attached scene host return neutral values for these helpers.

| Function | Inputs | Returns |
| --- | --- | --- |
| `key_down(keycode)` | `integer` key code | `boolean` |
| `pointer_world_x()` | none | `real` |
| `pointer_world_y()` | none | `real` |
| `pointer_screen_x()` | none | `real` |
| `pointer_screen_y()` | none | `real` |
| `pointer_is_down()` | none | `boolean` |
| `pointer_was_pressed()` | none | `boolean` |
| `pointer_was_released()` | none | `boolean` |

Pointer input tracks one primary pointer: left mouse button, primary pen button, or first/primary touch. Screen coordinates are HUD-space coordinates from the visible canvas top-left; world coordinates match the current `draw()` world view. Prefer `engine.input` wrappers for new scene code.

## Generated Web Scene Diagnostics Intrinsics

Generated web apps expose last-completed-frame diagnostics. Native execution and web execution without an attached scene host return neutral zero values for these helpers.

| Function | Inputs | Returns |
| --- | --- | --- |
| `diagnostics_last_frame_interval_milliseconds()` | none | `real` |
| `diagnostics_estimated_frames_per_second()` | none | `real` |
| `diagnostics_last_frame_work_milliseconds()` | none | `real` |
| `diagnostics_last_update_work_milliseconds()` | none | `real` |
| `diagnostics_last_draw_work_milliseconds()` | none | `real` |
| `diagnostics_last_draw_hud_work_milliseconds()` | none | `real` |
| `diagnostics_last_update_steps()` | none | `integer` |

These values measure browser runtime/VM work around update, draw, and HUD invocation. They do not include browser compositor or GPU presentation time. Prefer `engine.diagnostics` wrappers for new app code.

## Generated Web Scene Audio Intrinsics

Generated web apps expose asset-backed audio helpers. Native execution and web execution without an attached scene host return neutral values and perform no playback.

| Function | Inputs | Returns |
| --- | --- | --- |
| `audio_can_play_sound()` | none | `boolean` |
| `audio_play_sound(source, volume)` | `string`, `real` | `integer` handle |
| `audio_play_looping_sound(source, volume)` | `string`, `real` | `integer` handle |
| `audio_stop_sound(handle)` | `integer` | `void` |
| `audio_set_sound_volume(handle, volume)` | `integer`, `real` | `void` |
| `audio_sound_is_playing(handle)` | `integer` | `boolean` |
| `audio_stop_all_sounds()` | none | `void` |

The browser runtime uses static asset paths, lazy loading, and browser audio unlock on first key or pointer input. Prefer `engine.audio` wrappers for new app code.

## Target Restrictions

Compile target is selected with:

```powershell
compiler --target vm-web --compile-only ConsoleApp1/examples/time.code
```

Native-only calls rejected on `vm-web`:

- `read_line()`
- `sleep_ms(ms)`

Capability checks also consider package/import namespaces and manifest `hostAbi.requires`.
