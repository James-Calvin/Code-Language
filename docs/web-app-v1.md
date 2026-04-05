# Web App Runtime V1 Contract

Last updated: 2026-04-05
Status: implemented in a first working slice; broader engine/runtime expansion is still in progress

## Purpose

This document freezes the first end-to-end browser app contract for Code and tracks the first working implementation slice.

The goal of V1 is narrow:
- write a Code app as a scene object
- build it for the web
- receive a deployable static site folder
- open `index.html` and get a full-window interactive 2D app

This document defines the contract that the current first slice implements and that future engine/package work must preserve.

## Product Defaults

- Primary target: `vm-web`
- Primary workload: 2D interactive applications and games
- Authoring model: scene object
- Browser presentation: fills the browser window by default
- Coordinate model: guaranteed safe area of `640x360`, with hybrid-expanded world framing beyond that safe area when needed
- Initial rendering/input scope: shapes + keyboard input only
- Build output: deployable static site folder
- Default output directory: `dist/`

Output directory rules:
- If a nearest `code.package.json` exists, emit `dist/` in the package root by default.
- If no manifest exists, emit `dist/` beside the entry `.code` file by default.

## Current vs Planned

Current state:
- The compiler can target `vm-web`.
- A dedicated web build mode exists: `--build-web <entry.code>`.
- The default web build output is `dist/`, unless `--out` is provided.
- The generated app page owns the browser canvas and runtime bootstrap.
- The current browser-backed V1 slice supports `MainScene`, `start()`, `update()`, `draw()`, optional `draw_hud()`, full-window presentation, hybrid-expanded framing around a fixed `640x360` safe area, `key_down()`, `clear()`, `draw_rect()`, `camera_view_*()`, `camera_safe_*()`, `screen_width()`, and `screen_height()`.
- `web-runtime/index.html` still exists as a lower-level harness for loading raw `.bytecode` / `.codelib` files during debugging and bring-up.
- Legacy window-handle engine host bindings still exist, but they are not the default scene-object workflow.

Planned V1 behavior:
- Keep the current scene-object/browser contract stable while moving higher-level engine packages and wrappers onto it.
- Expand beyond shapes + keyboard input without forcing raw browser/bootstrap concerns into user code.
- Reduce reliance on the lower-level upload harness in day-to-day development.

## Scene Object Contract

V1 uses a convention over the existing object model. No new scene syntax is introduced.

Entry convention:
- The entry module for a V1 web app must export an object named `MainScene`.
- `MainScene` must have a zero-argument constructor.

Lifecycle:
- The runtime instantiates `MainScene` once.
- The runtime calls `start()` exactly once after scene creation and before the first update.
- The runtime calls `update()` on a fixed-step simulation loop at 60 updates per second.
- The runtime calls `draw()` once per presented frame.
- If present, the runtime calls `draw_hud()` once per presented frame after `draw()`.

Required methods:
- `start()`
- `update()`
- `draw()`

Optional method:
- `draw_hud()`

Method intent:
- `start()` is for initialization that depends on the runtime being ready.
- `update()` is for simulation, state changes, and input-driven gameplay logic.
- `draw()` is for rendering the current world/gameplay state.
- `draw_hud()` is for screen-edge-attached HUD or overlay work that should not move with the expanded world view.

Important implementation note:
- Object methods now support the same implicit-void authoring style as top-level functions.
- The scene lifecycle is therefore expressed directly as `function start()`, `function update()`, and `function draw()`.

Example target authoring shape:

```code
export object MainScene {
  integer x;
  integer y;

  constructor() {
    this.x = 100;
    this.y = 100;
  }

  function start() {
  }

  function update() {
    if key_down(37) then this.x = this.x - 2;
    if key_down(39) then this.x = this.x + 2;
    if key_down(38) then this.y = this.y - 2;
    if key_down(40) then this.y = this.y + 2;
  }

  function draw() {
    clear(0, 0, 0, 1);
    if this.x > camera_view_left() - 24 and this.x < camera_view_right() then {
      draw_rect(this.x, this.y, 24, 24, 1, 1, 1, 1);
    }
  }

  function draw_hud() {
    draw_rect(screen_width() - 44, 12, 32, 16, 1, 1, 1, 1);
  }
}
```

The example above matches the current first implementation slice and is checked in as `ConsoleApp1/examples/web_scene.code`.

## Runtime Behavior

Browser ownership:
- The generated app creates and owns the browser canvas.
- The app fills the browser window by default.
- The runtime, not user code, handles resize and presentation.

Virtual resolution:
- The guaranteed safe area is always `0,0 -> 640,360`.
- Scene code can treat that rectangle as the authored gameplay-safe region.
- Browser resize does not change the safe area.

Scaling:
- The browser canvas fills the browser viewport edge-to-edge with no letterboxing.
- Aspect ratio is preserved.
- The visible world expands around the `640x360` safe area instead of stretching or cropping.
- On wider screens, visible world width grows while visible world height remains `360`.
- On taller screens, visible world height grows while visible world width remains `640`.
- The safe area stays centered inside the expanded visible world.

World vs HUD spaces:
- `draw()` uses world-space coordinates in the expanded visible world rectangle.
- `draw_hud()` uses screen-space coordinates anchored to the visible browser edges.
- HUD origin is top-left of the visible screen.
- HUD size is exposed through `screen_width()` and `screen_height()`.

Loop behavior:
- `update()` runs at a fixed 60 Hz step.
- `draw()` runs once per presented frame.
- `draw_hud()` runs once per presented frame after `draw()` when present.
- If rendering is slower than updates for a short period, simulation remains fixed-step and presentation may skip frames rather than change game speed.

## V1 API Surface

The V1 scene runtime hides raw window-handle management in the default workflow.

Required V1 surface:
- `clear(real r, real g, real b, real a)`
- `draw_rect(real x, real y, real w, real h, real r, real g, real b, real a)`
- `key_down(integer keycode) -> boolean`
- `camera_view_left() -> real`
- `camera_view_top() -> real`
- `camera_view_width() -> real`
- `camera_view_height() -> real`
- `camera_view_right() -> real`
- `camera_view_bottom() -> real`
- `camera_safe_left() -> real`
- `camera_safe_top() -> real`
- `camera_safe_width() -> real`
- `camera_safe_height() -> real`
- `camera_safe_right() -> real`
- `camera_safe_bottom() -> real`
- `screen_width() -> real`
- `screen_height() -> real`

Behavior rules:
- `clear(...)` clears the full visible browser canvas for the current frame.
- `draw_rect(...)` draws in world coordinates during `draw()`.
- `draw_rect(...)` draws in screen-space coordinates during `draw_hud()`.
- `key_down(...)` returns the current keyboard state without requiring a window handle.
- `camera_view_*()` exposes the current expanded visible world bounds.
- `camera_safe_*()` exposes the guaranteed `640x360` safe area bounds.
- `screen_width()` / `screen_height()` expose the visible HUD/screen-space size.
- Peek limiting, culling, and gameplay-specific visibility rules remain developer-authored in user code; the runtime only exposes the bounds needed to implement them.

Out of scope for V1:
- sprite/image loading
- text rendering
- audio
- mouse/touch input
- physics
- GPU abstraction work
- editor tooling

## Web Build Contract

The web build flow must produce a deployable static site folder.

Required output:
- `index.html`
- compiled program artifact(s)

Current implementation output:
- `index.html`
- `app.bytecode`
- The runtime loader is currently inlined into `index.html`.

Required behavior:
- Opening `dist/index.html` runs the app directly.
- The generated page is the app page, not a developer harness or file-upload UI.
- The build output is static-host friendly and can be served by any basic static host.

Developer-experience rule:
- The primary web workflow must not require opening `web-runtime/index.html`.
- The primary web workflow must not require selecting a `.bytecode` file manually in the browser.

Current CLI:

```text
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --build-web ConsoleApp1/examples/web_scene.code
```

## Acceptance Criteria

The first implementation milestone is defined by the following conditions:
- A sample scene-object app builds to `dist/`.
- Opening `dist/index.html` runs without a manual upload step.
- The app fills the browser window.
- Rendering preserves aspect ratio with a centered `640x360` safe area and hybrid-expanded visible world.
- Keyboard input works inside `update()`.
- Rectangle rendering works inside `draw()`.
- HUD anchoring works inside `draw_hud()` using `screen_width()` / `screen_height()`.

Implementation note:
- Automated coverage exists for bytecode generation, scene metadata extraction, and generated `index.html` contract.
- Broader browser/manual validation should continue as the runtime surface expands.

## Non-Goals for This Document

This document does not define:
- final package names for engine wrappers
- editor or IDE integration
- sprite/audio APIs
- native app shell behavior beyond keeping portability in mind
- bytecode or VM opcode changes

Those can evolve later, but they must not violate the runtime contract defined here.
