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
- Initial rendering/input scope: primitive drawing (`draw_rectangle`, outlines, lines, circles, polygons, text), image/sprite drawing, and keyboard input
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
- The current browser-backed V1 slice supports `MainScene`, `start()`, `update()`, `draw()`, optional `draw_hud()`, full-window presentation, hybrid-expanded framing around a fixed `640x360` safe area, `key_down()`, `clear()`, `draw_rectangle()`, `draw_rectangle_outline()`, `draw_line()`, `draw_circle()`, `draw_circle_outline()`, `draw_polygon()`, `draw_polygon_outline()`, `draw_text()`, `draw_image()`, `draw_sprite()`, `camera_view_*()`, `camera_safe_*()`, `screen_width()`, and `screen_height()`.
- A higher-level wrapper layer now exists under `lib/engine/`: canonical modules `engine.colors`, `engine.drawing`, `engine.input`, `engine.viewport`, and `engine.scene`, with compatibility re-export modules `engine.view` and `engine.loop`.
- Scene composition is now supported through explicit child-object registration against `Scene`.
- `web-runtime/index.html` still exists as a lower-level harness for loading raw `.bytecode` / `.codelib` files during debugging and bring-up.
- Legacy window-handle engine host bindings still exist, but they are not the default scene-object workflow.

Planned V1 behavior:
- Keep the current scene-object/browser contract stable while expanding the wrapper layer on top of it.
- Expand beyond the current primitive/image-sprite/keyboard slice without forcing raw browser/bootstrap concerns into user code.
- Reduce reliance on the lower-level upload harness in day-to-day development.
- Add a target-agnostic graphical app profile that can synthesize the entry shell from top-level lifecycle authoring and an implicit engine prelude for `--build-web` first, without removing explicit `MainScene`.
- Prevent generated-page scroll/panning, including arrow/space key browser defaults, so game input does not move the page.
- Route normal web-app `print` output to the browser console by default; reserve on-screen output for fatal/runtime diagnostics or explicit debug mode.

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

Scene composition:
- `MainScene` remains the required exported entry object for web builds.
- Larger projects are now expected to keep `MainScene` thin and register child objects through `engine.scene.Scene`.
- Child-object lifecycle is split across `Startable`, `Updatable`, `WorldDrawable`, and `HudDrawable`.
- Registration is explicit; there is no field auto-discovery in V1.
- Registration changes are staged and applied at the start of the next `update()` phase.
- `SceneLoop` is now part of the canonical `engine.scene` public surface; `engine.loop` remains as a temporary compatibility re-export.

Planned app-profile direction:
- Explicit `MainScene` remains valid.
- A future graphical app profile should allow top-level `start()`, `update()`, `draw()`, and optional `draw_hud()` authoring with an implicit engine prelude.
- The profile should be target-agnostic so future native graphical targets can run the same Code source as the web target.

Important implementation note:
- Object methods now support the same implicit-void authoring style as top-level functions.
- Object constructors and methods also support implicit `this` lookup for unshadowed fields and bare method calls.
- Interface methods can now be implemented inline inside object bodies with `implement InterfaceName.methodName(...) { ... }`.
- Function-heavy wrapper modules can be imported as compile-time namespaces with `import everything as Draw from "engine/drawing.code";`.
- The scene lifecycle is therefore expressed directly as `function start()`, `function update()`, and `function draw()`.

Example target authoring shape:

```code
import everything as Draw from "engine/drawing.code";
import everything as Viewport from "engine/viewport.code";
import { rgb, rgba } from "engine/colors.code";
import { key_is_down } from "engine/input.code";
import { HudDrawable, Scene, SceneLoop, Updatable, WorldDrawable } from "engine/scene.code";

object Player {
  integer x;
  integer y;
  integer speed;

  constructor() {
    x = 100;
    y = 100;
    speed = 2;
  }

  implement Updatable.update() {
    if key_is_down(37) then x -= speed;
    if key_is_down(39) then x += speed;
    if key_is_down(38) then y -= speed;
    if key_is_down(40) then y += speed;
  }

  implement WorldDrawable.draw() {
    if x > Viewport.view_left() - 24 and x < Viewport.view_right() then {
      Draw.rectangle(x, y, 24, 24, rgb(1, 1, 1));
      Draw.rectangle_outline(x - 4, y - 4, 32, 32, 2, rgba(1. / 4, 1. / 2, 1, 2. / 3));
    }
  }
}

object BackgroundLayer {
  constructor() {
  }

  implement WorldDrawable.draw() {
    Draw.clear_screen(rgb(0, 0, 0));
    Draw.line(Viewport.safe_left(), Viewport.safe_top(), Viewport.safe_right(), Viewport.safe_bottom(), rgba(1, 1, 1, 1. / 3));
    Draw.polygon({300, 80, 340, 92, 352, 120, 304, 124, 284, 100}, rgba(0, 1. / 2, 1, 1. / 3));
    Draw.polygon_outline({300, 80, 340, 92, 352, 120, 304, 124, 284, 100}, 2, rgb(1, 1, 1));
    Draw.circle(124, 84, 16, rgba(1, 1. / 2, 1. / 4, 1. / 2));
    Draw.circle_outline(124, 84, 24, 2, rgb(1, 1, 1));
    Draw.image("assets/code-sheet.svg", 24, 220, 64, 32, 1);
    Draw.sprite("assets/code-sheet.svg", 32, 0, 32, 32, 104, 210, 64, 64, 1);
  }
}

object HeadsUpDisplay {
  Player player;

  constructor(Player player) {
    this.player = player;
  }

  implement HudDrawable.draw_hud() {
    Draw.text("Code", 16, 16, 18, "left", "top", rgb(1, 1, 1));
    Draw.text("Arrow keys move", Viewport.hud_width() - 16, 16, 16, "right", "top", rgb(1, 1, 1));
    Draw.text("Player X: {player.x}", 16, 40, 14, "left", "top", rgb(1, 1, 1));
  }
}

export object MainScene {
  Scene scene;
  SceneLoop loop;
  BackgroundLayer background_layer;
  Player player;
  HeadsUpDisplay heads_up_display;

  constructor() {
    scene = new Scene();
    loop = new SceneLoop(scene);
    background_layer = new BackgroundLayer();
    player = new Player();
    heads_up_display = new HeadsUpDisplay(player);
  }

  function start() {
    scene.add_world_drawable(background_layer, 0);
    scene.add_updatable(player);
    scene.add_world_drawable(player, 10);
    scene.add_hud_drawable(heads_up_display, 0);
    loop.start();
  }

  function update() {
    loop.update();
  }

  function draw() {
    loop.draw();
  }

  function draw_hud() {
    loop.draw_hud();
  }
}
```

The example above matches the current recommended larger-project shape and is checked in as `ConsoleApp1/examples/web_scene.code`.

For a smaller playable sample that stays within primitives plus keyboard input, see `ConsoleApp1/examples/shape_dodge.code`. `shape_dodge.code` is the current recommended "small game" demo, while `web_scene.code` remains the broader scene-composition and rendering reference. Example status and usage are cataloged in `docs/example-catalog.md`.

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
- `engine.scene` registration changes are staged; adds/removes made during `update()`, `draw()`, or `draw_hud()` do not take effect until the next `update()` phase.

## V1 API Surface

The V1 scene runtime hides raw window-handle management in the default workflow.

Raw scene-runtime surface:
- `clear(real r, real g, real b, real a)`
- `draw_rectangle(real x, real y, real w, real h, real r, real g, real b, real a)`
- `draw_rectangle_outline(real x, real y, real w, real h, real line_width, real r, real g, real b, real a)`
- `draw_line(real x1, real y1, real x2, real y2, real r, real g, real b, real a)`
- `draw_circle(real x, real y, real radius, real r, real g, real b, real a)`
- `draw_circle_outline(real x, real y, real radius, real line_width, real r, real g, real b, real a)`
- `draw_polygon(array points, real r, real g, real b, real a)`
- `draw_polygon_outline(array points, real line_width, real r, real g, real b, real a)`
- `draw_text(string text, real x, real y, real size, string horizontal_alignment, string vertical_alignment, real r, real g, real b, real a)`
- `draw_image(string source, real x, real y, real width, real height, real alpha)`
- `draw_sprite(string source, real source_x, real source_y, real source_width, real source_height, real x, real y, real width, real height, real alpha)`
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

Current wrapper layer:
- `engine.colors`
  - `rgb(real red, real green, real blue) -> Color`
  - `rgba(real red, real green, real blue, real alpha) -> Color`
- `engine.drawing`
  - `clear_screen(Color color)`
  - `line(...)`
  - `rectangle(...)`
  - `rectangle_outline(...)`
  - `circle(...)`
  - `circle_outline(...)`
  - `polygon(...)`
  - `polygon_outline(...)`
  - `text(...)`
  - `image(...)`
  - `sprite(...)`
- `engine.input`
  - `key_is_down(integer keycode) -> boolean`
- `engine.viewport`
  - `view_*()`
  - `safe_*()`
  - `hud_width()`
  - `hud_height()`
- `engine.scene`
  - `Startable`, `Updatable`, `WorldDrawable`, `HudDrawable`
  - `Scene`
  - `SceneLoop`
  - explicit staged registration methods for start/update/world-draw/hud-draw lifecycles
- Compatibility modules
  - `engine.view`
  - `engine.loop`

Behavior rules:
- `clear(...)` clears the full visible browser canvas for the current frame.
- `draw_rectangle(...)`, `draw_rectangle_outline(...)`, `draw_line(...)`, `draw_circle(...)`, `draw_circle_outline(...)`, `draw_polygon(...)`, `draw_polygon_outline(...)`, `draw_text(...)`, `draw_image(...)`, and `draw_sprite(...)` draw in world coordinates during `draw()`.
- The same drawing calls use screen-space coordinates during `draw_hud()`.
- `key_down(...)` returns the current keyboard state without requiring a window handle.
- `camera_view_*()` exposes the current expanded visible world bounds.
- `camera_safe_*()` exposes the guaranteed `640x360` safe area bounds.
- `screen_width()` / `screen_height()` expose the visible HUD/screen-space size.
- `draw_text(...)` uses alignment strings: horizontal `"left"`, `"center"`, `"right"` and vertical `"top"`, `"middle"`, `"bottom"`.
- `draw_polygon(...)` / `draw_polygon_outline(...)` take a flat numeric array of alternating `x, y` points.
- `draw_image(...)` and `draw_sprite(...)` load from static asset paths in the built site folder.
- Future byte-channel `rgb(byte, byte, byte)` and `rgba(byte, byte, byte, byte)` overloads should build on the implemented `byte` / `whole8` numeric type surface; current color wrappers use real channels commonly from `0` to `1`.
- Integral `/` is truncating integer division. Use a `real` operand for ratio values, for example `1. / 4` or `1 as real / 4`.
- Legacy `draw_rect(...)` remains accepted as a temporary compatibility alias, but docs and examples use `draw_rectangle(...)`.
- Peek limiting, culling, and gameplay-specific visibility rules remain developer-authored in user code; the runtime only exposes the bounds needed to implement them.

Out of scope for V1:
- audio
- mouse/touch input
- physics
- GPU abstraction work
- editor tooling
- explicit font family/style selection beyond the runtime default font

## Web Build Contract

The web build flow must produce a deployable static site folder.

Required output:
- `index.html`
- compiled program artifact(s)

Current implementation output:
- `index.html`
- `app.bytecode`
- copied `assets/` directory when present beside the entry file or in the package root
- The runtime loader is currently inlined into `index.html`.
- The compiled bytecode is also embedded in `index.html` today so direct opening does not require a fetch of `app.bytecode`.
- The browser VM/runtime is currently JavaScript. Wasm is deferred until measured performance or parity work justifies the extra build/tooling complexity.

Planned output polish:
- Default to embed-only bytecode so `index.html` remains directly openable without also writing a duplicate `app.bytecode`.
- Add or use a debug/inspection flag to emit `app.bytecode` when separate artifact inspection is useful.

Required behavior:
- Opening `dist/index.html` runs the app directly.
- The generated page is the app page, not a developer harness or file-upload UI.
- The build output is static-host friendly and can be served by any basic static host.
- Relative asset paths such as `assets/code-sheet.svg` remain valid in the generated output.

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
- Rectangle, outline, circle, polygon, text, and image/sprite rendering work inside `draw()` / `draw_hud()`.
- HUD anchoring works inside `draw_hud()` using `screen_width()` / `screen_height()`.
- The first wrapper layer under `lib/engine/` covers colors, drawing, input, and view queries for scene apps.

Implementation note:
- Automated coverage exists for bytecode generation, scene metadata extraction, and generated `index.html` contract.
- Broader browser/manual validation should continue as the runtime surface expands.

## Non-Goals for This Document

This document does not define:
- the broader engine package taxonomy beyond the current `engine.colors`, `engine.drawing`, `engine.input`, `engine.viewport`, and `engine.scene` wrappers
- editor or IDE integration
- audio APIs
- native app shell behavior beyond keeping portability in mind
- bytecode or VM opcode changes

Those can evolve later, but they must not violate the runtime contract defined here.

