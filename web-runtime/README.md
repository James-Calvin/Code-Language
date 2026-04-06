# Code Web Runtime Harness

This folder contains the current preview browser runtime harness for Code bytecode.
- It exists to bootstrap and debug web-target execution beside the generated web app flow.
- It is no longer the primary browser workflow.
- The planned replacement is documented in `docs/web-app-v1.md`.

Current contents:
- `code-vm-web.js`: JavaScript VM + web host ABI bindings
- `index.html`: load and run `.bytecode` or `.codelib` files in a browser

Current VM data-structure support:
- Arrays plus built-in `map`, `set`, `queue`, and `stack` bytecode operations
- Shared `.length` behavior across arrays and the built-in collections

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
- `engine.window.camera_view_*_scene`
- `engine.window.camera_safe_*_scene`
- `engine.window.screen_width_scene`
- `engine.window.screen_height_scene`

Compatibility note:
- The runtime still accepts legacy aliases such as `std.io.*` and `engine.gfx.draw_rect_scene` so older compiled artifacts continue to run during the rename window.

## Primary workflow
1. Build a scene app:
   - `dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --build-web ConsoleApp1/examples/web_scene.code`
2. Open the generated `dist/index.html` directly, or serve the generated folder from any static host.

The generated web app path is now the main workflow for browser apps.
- Current generated runtime behavior: full-bleed browser canvas, centered `640x360` safe area, hybrid-expanded world framing, optional `draw_hud()` for screen-edge HUD work, scene primitives for rectangles, outlines, lines, circles, polygons, text, images, and sprites, and copied `assets/` content in the generated site output when present.
- The repo also ships a wrapper layer in `lib/engine/` so scene apps can import canonical modules `engine.colors`, `engine.drawing`, `engine.input`, `engine.viewport`, and `engine.scene` instead of relying on the raw helper surface. `engine.view` and `engine.loop` remain as compatibility re-exports.
- The generated runtime is currently JavaScript, not Wasm. Wasm remains a future option if performance or parity data justifies the extra build complexity.

## Harness quick start
1. Compile a program for web:
   - `dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --target vm-web --compile-only ConsoleApp1/examples/host_abi_basic.code`
2. Serve this repo root as static files (example):
   - `python -m http.server 8000`
3. Open `http://localhost:8000/web-runtime/index.html`
4. Load the generated `.bytecode` file and click **Run**.

This file-picker flow remains a temporary low-level path for raw bytecode bring-up and debugging. For normal browser-app development, use `--build-web`.

