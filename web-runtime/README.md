# Code Web Runtime Harness

This folder contains the current preview browser runtime harness for Code bytecode.
- It exists to bootstrap and debug web-target execution beside the generated web app flow.
- It is no longer the primary browser workflow.
- The generated web app contract is documented in `docs/web-app-v1.md`.

Current contents:
- `code-vm-web.js`: worker scheduling, browser host bindings, Wasm adapter, and reference JavaScript VM
- `code-runtime.wasm`: required Rust bytecode-v11 VM for generated applications
- `index.html`: load and run `.bytecode` or `.codelib` files in a browser

Current VM data-structure support:
- Arrays plus built-in `map`, `set`, `queue`, and `stack` bytecode operations
- Shared `.length` behavior across arrays and the built-in collections
- Recoverable `fallible<Value, ErrorCode>` success/error value opcodes

## Supported host ABI (web)
- `standard.input_output.print`
- `std.time.unix_ms`
- `std.time.unix_us`
- `std.time.mono_ns`
- `std.time.mono_ticks`
- `std.time.mono_ticks_per_second`
- `std.math.minimum`
- `std.math.maximum`
- `std.math.absolute`
- `std.math.sign`
- `std.math.lerp`
- `std.math.sine`
- `std.math.cosine`
- `std.math.square_root`
- `std.math.random`

Native-only host calls are present with explicit runtime diagnostics:
- `standard.input_output.read_line` -> `HostBindingError` on `vm-web`
- `std.time.sleep_ms` -> `HostBindingError` on `vm-web`

Engine ABI stubs are available as no-ops on web:
- `engine.window.create`
- `engine.window.should_close`
- `engine.window.present`
- `engine.input.key_down`
- `engine.gfx.clear`
- `engine.gfx.draw_rect`

Current generated scene-runtime bindings also support:
- `engine.input.key_down_scene`
- `engine.input.pointer_world_x_scene`
- `engine.input.pointer_world_y_scene`
- `engine.input.pointer_screen_x_scene`
- `engine.input.pointer_screen_y_scene`
- `engine.input.pointer_is_down_scene`
- `engine.input.pointer_was_pressed_scene`
- `engine.input.pointer_was_released_scene`
- `engine.gfx.draw_rectangle_scene`
- `engine.gfx.draw_rectangle_outline_scene`
- `engine.gfx.draw_line_scene`
- `engine.gfx.draw_circle_scene`
- `engine.gfx.draw_circle_outline_scene`
- `engine.gfx.draw_polygon_scene`
- `engine.gfx.draw_polygon_outline_scene`
- `engine.gfx.draw_text_scene`
- `engine.gfx.draw_image_scene`
- `engine.gfx.draw_sprite_scene`
- `engine.diagnostics.last_frame_interval_milliseconds_scene`
- `engine.diagnostics.estimated_frames_per_second_scene`
- `engine.diagnostics.last_frame_work_milliseconds_scene`
- `engine.diagnostics.last_update_work_milliseconds_scene`
- `engine.diagnostics.last_draw_work_milliseconds_scene`
- `engine.diagnostics.last_draw_hud_work_milliseconds_scene`
- `engine.diagnostics.last_update_steps_scene`
- `engine.diagnostics.last_dropped_update_steps_scene`
- `engine.audio.can_play_sound_scene`
- `engine.audio.play_sound_scene`
- `engine.audio.play_looping_sound_scene`
- `engine.audio.stop_sound_scene`
- `engine.audio.set_sound_volume_scene`
- `engine.audio.sound_is_playing_scene`
- `engine.audio.stop_all_sounds_scene`
- `engine.window.camera_view_*_scene`
- `engine.window.camera_safe_*_scene`
- `engine.window.screen_width_scene`
- `engine.window.screen_height_scene`

Compatibility note:
- The runtime still accepts legacy ABI symbols such as `std.io.*` and `engine.gfx.draw_rect_scene` so older compiled artifacts continue to run. These are not current source-level aliases.

## Primary workflow
1. Build a scene app:
   - `compiler ConsoleApp1/examples/web_scene.code`
2. Open the generated `web_scene/index.html` directly, or serve the generated folder from any static host.

The generated web app path is now the main workflow for browser apps.
- Current generated runtime behavior: full-bleed browser canvas, centered `640x360` safe area, hybrid-expanded world framing, optional `drawHud()` for screen-edge HUD work, scene primitives for rectangles, outlines, lines, circles, polygons, text, images, and sprites, keyboard and primary pointer input, asset-backed one-shot/looping audio, last-completed-frame diagnostics, copied `assets/` content in the generated site output when present, app-key scroll prevention, canvas touch gesture suppression, and normal `print` output routed to the browser console.
- Audio uses `HTMLAudioElement`, lazy asset paths, integer playback handles, volume clamping, and browser audio unlock on first key or pointer input. It is not a full mixer yet.
- Diagnostics measure runtime/VM work around update, draw, and HUD invocation. They do not include browser compositor or GPU presentation time.
- Generated apps run the Rust/Wasm bytecode VM, lifecycle functions, and update scheduling in a dedicated worker by default. The worker source is embedded and launched through a Blob URL, so a generated `index.html` still runs directly from `file://`.
- Fixed 60 Hz updates are the default. Fixed updates use an exact simulation delta, service one update per worker task, catch up for at most five consecutive turns, and report discarded steps. `Runtime.useContinuousUpdates()` explicitly selects one atomic update per cooperative worker turn.
- The main thread owns DOM input, audio, `requestAnimationFrame`, and Canvas replay. A worker draw produces a transferable numeric command buffer plus a string table; only one draw request may be in flight, preventing command-buffer backlog.
- Generated apps enable the opt-in VM profiler with `?code-profile=1`; worker-backed `CodeRuntime.profile.start()`, `stop()`, `reset()`, `report()`, and `json()` return promises. `await CodeRuntime.profile.report()` prints instruction, function, host-call, allocation, stack, and direct-Wasm GC-mode metrics where available.
- Generated web builds emit a cacheable `code-runtime.wasm`, embed a base64 fallback for direct-file execution, and embed bytecode in `index.html`. Maintainer builds can use `--emit-web-bytecode` to also write `app.bytecode`.
- The repo also ships a wrapper layer in `lib/engine/` including `engine.runtime` for update/render scheduling.
- The generated runtime is a bytecode-v11 Rust/Wasm VM with predecoded instructions, contiguous stacks, 16-byte tagged values, handle-backed collections, slot-backed fields, record hash-field metadata, and tracing garbage collection. The JavaScript VM remains a reference/conformance path.
- Maintainer builds can select `--web-backend direct-wasm` to emit `code-app.wasm`. Direct-Wasm is the active performance/runtime focus but remains gated behind compatibility and parity checks. Diagnostic direct-Wasm builds may use `--disable-garbage-collection`; that mode is reported by the direct-Wasm profiler and may leak memory.
- Browser compatibility validation lives in `scripts/test-browser-compat.mjs`. It automates local Chromium-family desktop browsers and writes `mobile-report.html` for manual iOS/Android browser reports.

## Harness quick start
1. Compile a program for web:
   - `compiler --target vm-web --compile-only ConsoleApp1/examples/host_abi_basic.code`
2. Serve this repo root as static files (example):
   - `python -m http.server 8000`
3. Open `http://localhost:8000/web-runtime/index.html`
4. Load the generated `.bytecode` file and click **Run**.

This file-picker flow remains a temporary low-level path for raw bytecode bring-up and debugging. For normal browser-app development, use `compiler entry.code`.
