# Web App Runtime V1 Contract

Last updated: 2026-06-14
Status: implemented in a first working slice; broader engine/runtime expansion is still in progress

## Purpose

This document freezes the first end-to-end browser app contract for Code and tracks the first working implementation slice.

The goal of V1 is narrow:
- write a Code app as a scene object or top-level web app entry
- build it for the web
- receive a deployable static site folder
- open `index.html` and get a full-window interactive 2D app

This document defines the contract that the current first slice implements and that future engine/package work must preserve.

## Product Defaults

- Primary target: `vm-web`
- Primary workload: 2D interactive applications and games
- Authoring model: explicit scene object or inferred top-level lifecycle entry
- Browser presentation: fills the browser window by default
- Coordinate model: guaranteed safe area of `640x360`, with hybrid-expanded world framing beyond that safe area when needed
- Initial rendering/input/audio/diagnostics scope: primitive drawing (`drawRectangle`, outlines, lines, circles, polygons, text), image/sprite drawing, keyboard input, primary pointer input, asset-backed one-shot/looping audio, and last-frame diagnostics
- Build output: deployable static site folder
- Default output directory: a folder in the current working directory named after the entry file, for example `shape_dodge/`

Output directory rules:
- `compiler path/to/source.code` emits `./source/` from the current working directory by default.
- `compiler path/to/source.code -o MyApp` emits `./MyApp/`.

## Current vs Planned

Current state:
- The compiler can target `vm-web`.
- Web build is the default public compiler behavior for `.code` input.
- The public output flag is `-o` / `--output`.
- The generated app page owns the browser canvas and runtime bootstrap.
- The current browser-backed V1 slice supports either an explicit `MainScene` object or an inferred top-level lifecycle entry with `start()`, `update()`, `draw()`, and optional `drawHud()`, full-window presentation, hybrid-expanded framing around a fixed `640x360` safe area, keyboard input, primary pointer input, `clear()`, `drawRectangle()`, `drawRectangleOutline()`, `drawLine()`, `drawCircle()`, `drawCircleOutline()`, `drawPolygon()`, `drawPolygonOutline()`, `drawText()`, `drawImage()`, `drawSprite()`, asset-backed one-shot/looping audio, `cameraView*()`, `cameraSafe*()`, `screenWidth()`, and last-frame diagnostics, plus usage-based implied engine imports across web app modules.
- A higher-level wrapper layer now exists under `lib/engine/`: canonical modules `engine.colors`, `engine.drawing`, `engine.input`, `engine.viewport`, `engine.diagnostics`, `engine.audio`, and `engine.scene`, with compatibility re-export modules `engine.view` and `engine.loop`.
- Scene composition is now supported through explicit child-object registration against `Scene`.
- Generated apps prevent browser scroll/panning for app-control keys: arrows, Space, Page Up, Page Down, Home, and End.
- Normal generated-app `print` output goes to the browser console by default; the on-screen overlay is reserved for fatal/runtime diagnostics.
- Web builds embed bytecode in `index.html` by default and only emit `app.bytecode` when `--emit-web-bytecode` is used.
- `web-runtime/index.html` still exists as a lower-level harness for loading raw `.bytecode` / `.codelib` files during debugging and bring-up.
- Legacy window-handle engine host bindings still exist, but they are not the default scene-object workflow.

Planned V1 behavior:
- Keep the current scene-object/browser contract stable while expanding the wrapper layer on top of it.
- Expand beyond the current primitive/image-sprite/primary-input slice without forcing raw browser/bootstrap concerns into user code.
- Reduce reliance on the lower-level upload harness in day-to-day development.
- Keep the current inferred web-entry slice stable while expanding it toward fuller target-agnostic reuse and keeping explicit `MainScene` available.

## Scene Object Contract

V1 uses a convention over the existing object model. No new scene syntax is introduced.

Supported entry shapes:
- Explicit scene object:
  - The entry module exports an object named `MainScene`.
  - `MainScene` has a zero-argument constructor.
- Inferred top-level lifecycle entry:
  - The web app entry module declares top-level `start()`, `update()`, and `draw()` functions, with optional `drawHud()`.
  - Top-level state declarations remain module globals with persistent VM storage.
  - Top-level helper functions remain top-level functions and can share same-module globals with lifecycle functions and object methods.
  - The compiler synthesizes an internal `MainScene` with lifecycle methods so the browser runtime contract stays unchanged.
  - Top-level executable statements are rejected in this entry shape.
  - Web app modules receive usage-based implied engine imports.
  - `Draw`, `Input`, `Viewport`, `Colors`, `Diagnostics`, `Runtime`, and `Audio` are available as implied namespaces.
  - Direct `Color`, `Scene`, `SceneLoop`, `Startable`, `Updatable`, `WorldDrawable`, and `HudDrawable` names are also available without explicit imports.
  - Bare engine functions such as `rectangle(...)` are not implied; use namespace style such as `Draw.rectangle(...)` or add an explicit import.
  - The namespace names `Draw`, `Input`, `Viewport`, `Colors`, `Diagnostics`, `Runtime`, and `Audio` are reserved for web-app modules. Redundant explicit imports of those exact canonical namespaces still work.

Lifecycle:
- The runtime instantiates `MainScene` once.
- The runtime calls `start()` exactly once after scene creation and before the first update.
- The runtime calls `update()` at a fixed 60 Hz by default.
- The runtime calls `draw()` once per presented frame.
- If present, the runtime calls `drawHud()` once per presented frame after `draw()`.

Required methods:
- `start()`
- `update()`
- `draw()`

Optional method:
- `drawHud()`

Method intent:
- `start()` is for initialization that depends on the runtime being ready.
- `update()` is for simulation, state changes, and input-driven gameplay logic.
- `draw()` is for rendering the current world/gameplay state.
- `drawHud()` is for screen-edge-attached HUD or overlay work that should not move with the expanded world view.

Scene composition:
- Explicit `MainScene` remains the advanced and compatibility path for web builds.
- Larger projects are now expected to keep `MainScene` thin and register child objects through `engine.scene.Scene`.
- Child-object lifecycle is split across `Startable`, `Updatable`, `WorldDrawable`, and `HudDrawable`.
- Registration is explicit; there is no field auto-discovery in V1.
- Registration changes are staged and applied at the start of the next `update()` phase.
- `SceneLoop` is now part of the canonical `engine.scene` public surface; `engine.loop` remains as a temporary compatibility re-export.

Current app-profile direction:
- Explicit `MainScene` remains valid.
- The first inferred profile slice is implemented for web entry modules with top-level `start()`, `update()`, `draw()`, and optional `drawHud()`. Top-level state remains same-module global storage, and the profile shares the same implied engine-import surface used by other web-app modules.
- The longer-term target is to carry that authoring shape toward broader target-agnostic reuse so future native graphical targets can run the same Code source.

Important implementation note:
- Object methods now support the same implicit-void authoring style as top-level functions.
- Object constructors and methods also support implicit `this` lookup for unshadowed fields and bare method calls.
- Interface methods can now be implemented inline inside object bodies with `implement InterfaceName.methodName(...) { ... }`.
- Function-heavy wrapper modules can be imported as compile-time namespaces with `import everything as Draw from "engine/drawing.code";`.
- The scene lifecycle is therefore expressed directly as `function start()`, `function update()`, and `function draw()`.

Small-app inferred-profile shape:

```code
integer x = 100;
integer y = 120;
integer speed = 2;

function start() {
}

function update() {
  if Input.keyIsDown(37) then x -= speed;
  if Input.keyIsDown(39) then x += speed;
}

function draw() {
  Draw.clearScreen(Colors.rgb(0, 0, 0));
  Draw.rectangle(x, y, 24, 24, Colors.rgb(255, 255, 255));
}
```

Advanced explicit-scene shape:

```code
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
    if Input.keyIsDown(37) then x -= speed;
    if Input.keyIsDown(39) then x += speed;
    if Input.keyIsDown(38) then y -= speed;
    if Input.keyIsDown(40) then y += speed;
    if Input.pointerWasPressed() then {
      x = Input.pointerWorldX() as integer;
      y = Input.pointerWorldY() as integer;
    }
  }

  implement WorldDrawable.draw() {
    if x > Viewport.viewLeft() - 24 and x < Viewport.viewRight() then {
      Draw.rectangle(x, y, 24, 24, Colors.rgb(255, 255, 255));
      Draw.rectangleOutline(x - 4, y - 4, 32, 32, 2, Colors.rgba(1. / 4, 1. / 2, 1, 2. / 3));
    }
  }
}

object BackgroundLayer {
  constructor() {
  }

  implement WorldDrawable.draw() {
    Draw.clearScreen(Colors.rgb(0, 0, 0));
    Draw.line(Viewport.safeLeft(), Viewport.safeTop(), Viewport.safeRight(), Viewport.safeBottom(), Colors.rgba(1, 1, 1, 1. / 3));
    Draw.polygon({300, 80, 340, 92, 352, 120, 304, 124, 284, 100}, Colors.rgba(0, 1. / 2, 1, 1. / 3));
    Draw.polygonOutline({300, 80, 340, 92, 352, 120, 304, 124, 284, 100}, 2, Colors.rgb(255, 255, 255));
    Draw.circle(124, 84, 16, Colors.rgba(1, 1. / 2, 1. / 4, 1. / 2));
    Draw.circleOutline(124, 84, 24, 2, Colors.rgb(255, 255, 255));
    Draw.image("assets/code-sheet.svg", 24, 220, 64, 32, 1);
    Draw.sprite("assets/code-sheet.svg", 32, 0, 32, 32, 104, 210, 64, 64, 1);
  }
}

object HeadsUpDisplay {
  Player player;

  constructor(Player player) {
    this.player = player;
  }

  implement HudDrawable.drawHud() {
    Draw.text("Code", 16, 16, 18, "left", "top", Colors.rgb(255, 255, 255));
    Draw.text("Arrow keys move", Viewport.hudWidth() - 16, 16, 16, "right", "top", Colors.rgb(255, 255, 255));
    Draw.text("Player X: {player.x}", 16, 40, 14, "left", "top", Colors.rgb(255, 255, 255));
    Draw.text("Pointer: {Input.pointerScreenX()}, {Input.pointerScreenY()}", 16, 64, 14, "left", "top", Colors.rgb(255, 255, 255));
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
    scene.addWorldDrawable(background_layer, 0);
    scene.addUpdatable(player);
    scene.addWorldDrawable(player, 10);
    scene.addHudDrawable(heads_up_display, 0);
    loop.start();
  }

  function update() {
    loop.update();
  }

  function draw() {
    loop.draw();
  }

  function drawHud() {
    loop.drawHud();
  }
}
```

The example above matches the current recommended larger-project shape and is checked in as `ConsoleApp1/examples/web_scene.code`.

For a smaller playable sample that uses the inferred web-entry profile, see `ConsoleApp1/examples/shape_dodge.code`. For audio, see `ConsoleApp1/examples/audio_demo.code`. For performance diagnostics, see `ConsoleApp1/examples/performance_dashboard.code`. `shape_dodge.code` is the current recommended "small game" demo and the easiest web-entry starting point, while `web_scene.code` remains the broader explicit-scene composition, rendering, assets, and pointer-input reference. Example status and usage are cataloged in `docs/example-catalog.md`.

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
- `drawHud()` uses screen-space coordinates anchored to the visible browser edges.
- HUD origin is top-left of the visible screen.
- HUD size is exposed through `screenWidth()` and `screenHeight()`.

Loop behavior:
- `update()` runs at a fixed 60 Hz by default. Fixed updates receive the exact configured timestep through `Diagnostics.updateDeltaMilliseconds()`.
- `draw()` runs once per presented frame.
- `drawHud()` runs once per presented frame after `draw()` when present.
- Fixed mode services one update per worker task, catches up for at most five consecutive turns, and reports discarded excess steps. Continuous mode is explicit opt-in and also services one atomic update per cooperative turn. Rendering remains independently `requestAnimationFrame`-driven.
- The generated runtime executes the VM and lifecycle methods in a dedicated worker. The worker sends transferable draw command buffers to the main thread, which owns Canvas, DOM input, audio, and visibility.
- `engine.scene` registration changes are staged; adds/removes made during `update()`, `draw()`, or `drawHud()` do not take effect until the next `update()` phase.

## V1 API Surface

The V1 scene runtime hides raw window-handle management in the default workflow.

Raw scene-runtime surface:
- `clear(real r, real g, real b, real a)`
- `drawRectangle(real x, real y, real w, real h, real r, real g, real b, real a)`
- `drawRectangleOutline(real x, real y, real w, real h, real lineWidth, real r, real g, real b, real a)`
- `drawLine(real x1, real y1, real x2, real y2, real r, real g, real b, real a)`
- `drawCircle(real x, real y, real radius, real r, real g, real b, real a)`
- `drawCircleOutline(real x, real y, real radius, real lineWidth, real r, real g, real b, real a)`
- `drawPolygon(array points, real r, real g, real b, real a)`
- `drawPolygonOutline(array points, real lineWidth, real r, real g, real b, real a)`
- `drawText(string text, real x, real y, real size, string horizontalAlignment, string verticalAlignment, real r, real g, real b, real a)`
- `drawImage(string source, real x, real y, real width, real height, real alpha)`
- `drawSprite(string source, real sourceX, real sourceY, real sourceWidth, real sourceHeight, real x, real y, real width, real height, real alpha)`
- `inputKeyDown(integer keycode) -> boolean`
- `inputPointerWorldX() -> real`
- `inputPointerWorldY() -> real`
- `inputPointerScreenX() -> real`
- `inputPointerScreenY() -> real`
- `inputPointerIsDown() -> boolean`
- `inputPointerWasPressed() -> boolean`
- `inputPointerWasReleased() -> boolean`
- `diagnosticsLastFrameIntervalMilliseconds() -> real`
- `diagnosticsEstimatedFramesPerSecond() -> real`
- `diagnosticsLastFrameWorkMilliseconds() -> real`
- `diagnosticsLastUpdateWorkMilliseconds() -> real`
- `diagnosticsLastDrawWorkMilliseconds() -> real`
- `diagnosticsLastDrawHudWorkMilliseconds() -> real`
- `diagnosticsLastUpdateSteps() -> integer`
- `diagnosticsLastDroppedUpdateSteps() -> integer`
- `diagnosticsLastUpdateIntervalMilliseconds() -> real`
- `diagnosticsUpdateDeltaMilliseconds() -> real`
- `runtimeUseContinuousUpdates()`
- `runtimeSetFixedUpdateRate(integer updatesPerSecond)`
- `runtimeSetMaximumRenderRate(integer framesPerSecond)`
- `runtimeUseDisplaySynchronizedRendering()`
- `audioCanPlaySound() -> boolean`
- `audioPlaySound(string source, real volume) -> integer`
- `audioPlayLoopingSound(string source, real volume) -> integer`
- `audioStopSound(integer handle)`
- `audioSetSoundVolume(integer handle, real volume)`
- `audioSoundIsPlaying(integer handle) -> boolean`
- `audioStopAllSounds()`
- `cameraViewLeft() -> real`
- `cameraViewTop() -> real`
- `cameraViewWidth() -> real`
- `cameraViewHeight() -> real`
- `cameraViewRight() -> real`
- `cameraViewBottom() -> real`
- `cameraSafeLeft() -> real`
- `cameraSafeTop() -> real`
- `cameraSafeWidth() -> real`
- `cameraSafeHeight() -> real`
- `cameraSafeRight() -> real`
- `cameraSafeBottom() -> real`
- `screenWidth() -> real`
- `screenHeight() -> real`

Current wrapper layer:
- `engine.colors`
  - `rgb(byte red, byte green, byte blue) -> Color`
  - `rgba(real red, real green, real blue, real alpha) -> Color`
- `engine.drawing`
  - `clearScreen(Color color)`
  - `line(...)`
  - `rectangle(...)`
  - `rectangleOutline(...)`
  - `circle(...)`
  - `circleOutline(...)`
  - `polygon(...)`
  - `polygonOutline(...)`
  - `text(...)`
  - `image(...)`
  - `sprite(...)`
- `engine.input`
  - `keyIsDown(integer keycode) -> boolean`
  - `pointerWorldX() -> real`
  - `pointerWorldY() -> real`
  - `pointerScreenX() -> real`
  - `pointerScreenY() -> real`
  - `pointerIsDown() -> boolean`
  - `pointerWasPressed() -> boolean`
  - `pointerWasReleased() -> boolean`
- `engine.diagnostics`
  - `lastFrameIntervalMilliseconds() -> real`
  - `estimatedFramesPerSecond() -> real`
  - `lastFrameWorkMilliseconds() -> real`
  - `lastUpdateWorkMilliseconds() -> real`
  - `lastDrawWorkMilliseconds() -> real`
  - `lastDrawHudWorkMilliseconds() -> real`
  - `lastUpdateSteps() -> integer`
  - `lastDroppedUpdateSteps() -> integer`
  - `lastUpdateIntervalMilliseconds() -> real`
  - `updateDeltaMilliseconds() -> real`
- `engine.runtime`
  - `useContinuousUpdates()`
  - `setFixedUpdateRate(integer updatesPerSecond)`
  - `setMaximumRenderRate(integer framesPerSecond)`
  - `useDisplaySynchronizedRendering()`
- `engine.audio`
  - `canPlaySound() -> boolean`
  - `playSound(string source, real volume) -> integer`
  - `playLoopingSound(string source, real volume) -> integer`
  - `stopSound(integer handle)`
  - `setSoundVolume(integer handle, real volume)`
  - `soundIsPlaying(integer handle) -> boolean`
  - `stopAllSounds()`
- `engine.viewport`
  - `view_*()`
  - `safe_*()`
  - `hudWidth()`
  - `hudHeight()`
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
- `drawRectangle(...)`, `drawRectangleOutline(...)`, `drawLine(...)`, `drawCircle(...)`, `drawCircleOutline(...)`, `drawPolygon(...)`, `drawPolygonOutline(...)`, `drawText(...)`, `drawImage(...)`, and `drawSprite(...)` draw in world coordinates during `draw()`.
- The same drawing calls use screen-space coordinates during `drawHud()`.
- `inputKeyDown(...)` returns the current keyboard state without requiring a window handle.
- `inputPointerScreenX()` / `inputPointerScreenY()` return HUD/screen-space coordinates from the visible canvas top-left.
- `inputPointerWorldX()` / `inputPointerWorldY()` return coordinates in the current expanded world view, matching `draw()` coordinates.
- `inputPointerIsDown()` tracks the primary pointer: left mouse button, primary pen button, or first/primary touch.
- `inputPointerWasPressed()` and `inputPointerWasReleased()` are fixed-update edge states intended for `update()`; a quick tap between updates can make both true for the next update.
- Last known pointer coordinates remain available after release; blur/cancel clears the down state and produces a release edge if needed.
- Frame and draw diagnostics describe the previous completed draw. During the current draw, they intentionally report the previous published values.
- `lastFrameWorkMilliseconds()` measures runtime VM work around update/draw/HUD invocation. It does not include browser compositor or GPU presentation time.
- `lastUpdateWorkMilliseconds()` describes one previous completed update and is not cleared by a draw that completed no updates. `lastUpdateSteps()` and dropped-step counts cover updates since the previous completed draw.
- `lastUpdateIntervalMilliseconds()` is measured wall-clock spacing between update starts. `updateDeltaMilliseconds()` is the integration timestep: exact `1000 / rate` in fixed mode and measured elapsed time in continuous mode. Physics should use the update delta, never the frame interval.
- Fixed updates are capped after five consecutive catch-up turns. Excess accumulated whole steps are discarded and exposed through `lastDroppedUpdateSteps()` to prevent an update spiral.
- Generated apps support opt-in VM profiling with `?code-profile=1`. Worker-backed `CodeRuntime.profile.start()`, `stop()`, `reset()`, `report()`, and `json()` return promises.
- Use `ConsoleApp1/examples/performance_dashboard.code` for relative threshold-finding, and browser devtools Performance for deeper browser/compositor investigation.
- Audio helpers use static asset paths in the built site folder, return integer handles for playback control, and use browser audio unlock on first key or pointer input.
- `playSound(...)` starts overlapping one-shot sounds; `playLoopingSound(...)` starts a loop suitable for background music. Missing or unsupported assets fail non-fatally and report not playing.
- Audio volume is clamped to `0..1`. Native execution and web execution without an attached scene host return neutral values and perform no playback.
- `cameraView*()` exposes the current expanded visible world bounds.
- `cameraSafe*()` exposes the guaranteed `640x360` safe area bounds.
- `screenWidth()` / `screenHeight()` expose the visible HUD/screen-space size.
- `drawText(...)` uses alignment strings: horizontal `"left"`, `"center"`, `"right"` and vertical `"top"`, `"middle"`, `"bottom"`.
- `drawPolygon(...)` / `drawPolygonOutline(...)` take a flat numeric array of alternating `x, y` points.
- `drawImage(...)` and `drawSprite(...)` load from static asset paths in the built site folder.
- `rgb(byte, byte, byte)` uses byte channels from `0` to `255`; `byte` and `whole8` are the same type.
- Future byte-channel `rgba(byte, byte, byte, byte)` should build on the implemented `byte` / `whole8` numeric type surface; current `rgba` still uses real channels commonly from `0` to `1`.
- Integral `/` is truncating integer division. Use a `real` operand for ratio values, for example `1. / 4` or `1 as real / 4`.
- `drawRectangle(...)` is the canonical rectangle intrinsic; old source-level abbreviation aliases are not part of the current public API.
- Peek limiting, culling, and gameplay-specific visibility rules remain developer-authored in user code; the runtime only exposes the bounds needed to implement them.

Out of scope for V1:
- lower-latency Web Audio mixing, buses, fades, panning, pitch, streamed decode controls, and guaranteed sample-accurate scheduling
- multi-touch ids, gestures, right/middle mouse buttons, wheel input, and pointer event queues
- physics
- GPU abstraction work
- editor tooling
- explicit font family/style selection beyond the runtime default font

## Web Build Contract

The web build flow must produce a deployable static site folder.

Required output:
- `index.html` containing embedded compiled bytecode

Current implementation output:
- `index.html`
- copied `assets/` directory when present beside the entry file or in the package root
- The runtime loader is currently inlined into `index.html`.
- The compiled bytecode is embedded in `index.html` so direct opening does not require a fetch of `app.bytecode`.
- `app.bytecode` is emitted only when the maintainer/debug flag `--emit-web-bytecode` is passed.
- The browser VM/runtime is currently worker-hosted JavaScript with decoded instructions, contiguous locals frames, and slot-backed object fields. A Wasm replacement remains gated on full parity, at least a 2x geometric-mean CPU benchmark improvement over this worker baseline, and a material Ball workload gain.

Required behavior:
- Opening the generated `index.html` runs the app directly.
- The generated page is the app page, not a developer harness or file-upload UI.
- The build output is static-host friendly and can be served by any basic static host.
- Relative asset paths such as `assets/code-sheet.svg` remain valid in the generated output.

Developer-experience rule:
- The primary web workflow must not require opening `web-runtime/index.html`.
- The primary web workflow must not require selecting a `.bytecode` file manually in the browser.

Current CLI:

```text
compiler ConsoleApp1/examples/shape_dodge.code
```

## Acceptance Criteria

The first implementation milestone is defined by the following conditions:
- A sample scene-object app builds to a folder named after the entry file.
- Opening the generated `index.html` runs without a manual upload step.
- The app fills the browser window.
- Rendering preserves aspect ratio with a centered `640x360` safe area and hybrid-expanded visible world.
- Keyboard input works inside `update()`.
- Primary pointer coordinates and press/release edges work inside `update()` and can be displayed in `drawHud()`.
- Last-frame diagnostics can be displayed in `drawHud()` through `engine.diagnostics` / `Diagnostics`.
- Asset-backed one-shot and looping audio can be controlled through `engine.audio` / `Audio`.
- Rectangle, outline, circle, polygon, text, and image/sprite rendering work inside `draw()` / `drawHud()`.
- HUD anchoring works inside `drawHud()` using `screenWidth()` / `screenHeight()`.
- The first wrapper layer under `lib/engine/` covers colors, drawing, input, diagnostics, audio, and view queries for scene apps.

Implementation note:
- Automated coverage exists for bytecode generation, scene metadata extraction, and generated `index.html` contract.
- Broader browser/manual validation should continue as the runtime surface expands.

## Non-Goals for This Document

This document does not define:
- the broader engine package taxonomy beyond the current `engine.colors`, `engine.drawing`, `engine.input`, `engine.viewport`, `engine.diagnostics`, `engine.audio`, and `engine.scene` wrappers
- editor or IDE integration
- full audio mixer APIs beyond the current asset-backed handle helpers
- native app shell behavior beyond keeping portability in mind
- bytecode or VM opcode changes

Those can evolve later, but they must not violate the runtime contract defined here.
