# Web Apps and Engine Modules

## Build a Web App

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --build-web ConsoleApp1/examples/shape_dodge.code
```

Output folder:

- `index.html`
- copied `assets/` folder when present

Current note:

- The generated `index.html` embeds the bytecode for direct opening. Use `--emit-web-bytecode` with `--build-web` to also write `app.bytecode` for debugging or inspection.
- Generated apps route normal `print` output to the browser console. The on-screen overlay is reserved for fatal/runtime diagnostics.
- Generated apps prevent browser scroll/panning for app-control keys such as arrows, Space, Page Up, Page Down, Home, and End.

Default output:

- package root `dist/` when a nearest `code.package.json` exists
- entry-file directory `dist/` when no manifest exists

Custom output:

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --build-web --out .tmp/web-demo ConsoleApp1/examples/web_scene.code
```

Common mistakes:

- `--build-web` requires a `.code` input.
- Module graph flags do not combine with `--build-web` yet.

## Web Entry Contract

A web app entry module may use either of these shapes:

Explicit scene object:

```code
export object MainScene {
  constructor() {
  }

  function start() {
  }

  function update() {
  }

  function draw() {
  }

  function draw_hud() {
  }
}
```

Inferred top-level lifecycle entry:

```code
integer x = 100;
integer speed = 2;

function start() {
}

function update() {
  if Input.key_is_down(39) then x += speed;
}

function draw() {
  Draw.rectangle(x, 100, 24, 24, Colors.rgb(1, 1, 1));
}
```

Rules:

- `start()`, `update()`, and `draw()` are required.
- `draw_hud()` is optional.
- The runtime creates `MainScene` once, either explicitly or from the synthesized inferred entry.
- `start()` runs once before updates.
- `update()` runs at a fixed 60 Hz step.
- `draw()` runs once per presented frame.
- `draw_hud()` runs after `draw()` when present.
- Inferred top-level lifecycle entry is a `--build-web` entry-module feature.
- Inferred entry modules get implicit namespace access to `Draw`, `Input`, `Viewport`, and `Colors`.

Common mistakes:

- Missing both valid entry shapes fails the web build.
- Keep explicit `MainScene` thin for larger apps and compose child objects through `engine.scene`.
- Top-level executable statements are rejected in inferred entry modules.

## Coordinates

The web runtime owns the browser canvas.

| Space | Used by | Notes |
| --- | --- | --- |
| World | `draw()` | Expanded visible world around the safe area |
| HUD | `draw_hud()` | Screen-space coordinates attached to browser edges |

The guaranteed safe area is always `640x360`. Wider or taller browser windows expand the visible world instead of stretching gameplay coordinates.

## Engine Colors

Import:

```code
import { Color, rgb, rgba } from "engine/colors.code";
```

API:

| Name | Inputs | Returns |
| --- | --- | --- |
| `Color` | fields `red`, `green`, `blue`, `alpha` | object |
| `rgb(red, green, blue)` | `real`, `real`, `real` | `Color` with alpha `1` |
| `rgba(red, green, blue, alpha)` | four `real` values | `Color` |

Example:

```code
Color white = rgb(1, 1, 1);
```

## Engine Drawing

Import as a namespace:

```code
import everything as Draw from "engine/drawing.code";
import { rgb } from "engine/colors.code";
```

API:

| Function | Inputs |
| --- | --- |
| `clear_screen(color)` | `Color` |
| `line(x1, y1, x2, y2, color)` | four `real`, `Color` |
| `rectangle(x, y, width, height, color)` | four `real`, `Color` |
| `rectangle_outline(x, y, width, height, line_width, color)` | five `real`, `Color` |
| `circle(x, y, radius, color)` | three `real`, `Color` |
| `circle_outline(x, y, radius, line_width, color)` | four `real`, `Color` |
| `polygon(points, color)` | `array<real>`, `Color` |
| `polygon_outline(points, line_width, color)` | `array<real>`, `real`, `Color` |
| `text(value, x, y, size, horizontal_alignment, vertical_alignment, color)` | `string`, three `real`, two `string`, `Color` |
| `image(source, x, y, width, height, alpha)` | `string`, four `real`, `real` |
| `sprite(source, source_x, source_y, source_width, source_height, x, y, width, height, alpha)` | `string`, nine `real` values |

Example:

```code
Draw.clear_screen(rgb(0, 0, 0));
Draw.rectangle(100, 80, 32, 32, rgb(1, 1, 1));
```

Common mistakes:

- Color channels are real values, commonly from `0` to `1`.
- Byte-channel color overloads such as `rgb(byte, byte, byte)` are planned on top of the implemented `byte` / `whole8` numeric type surface; do not write those overloads today.
- `polygon` points are a flat numeric array: `{x1, y1, x2, y2, ...}`.
- `text` alignment strings are `"left"`, `"center"`, `"right"` and `"top"`, `"middle"`, `"bottom"`.

## Engine Input

```code
import everything as Input from "engine/input.code";

if Input.key_is_down(37) then {
  print("left");
}

if Input.pointer_was_pressed_now() then {
  print("clicked or tapped at {Input.pointer_world_x_position()}, {Input.pointer_world_y_position()}");
}
```

API:

| Function | Inputs | Returns |
| --- | --- | --- |
| `key_is_down(keycode)` | `integer` key code | `boolean` |
| `pointer_world_x_position()` | none | `real` |
| `pointer_world_y_position()` | none | `real` |
| `pointer_screen_x_position()` | none | `real` |
| `pointer_screen_y_position()` | none | `real` |
| `pointer_is_down_now()` | none | `boolean` |
| `pointer_was_pressed_now()` | none | `boolean` |
| `pointer_was_released_now()` | none | `boolean` |

The current browser-backed input slice supports keyboard state plus one primary pointer. The primary pointer is the left mouse button, primary pen button, or first/primary touch. Screen coordinates are HUD-space coordinates from the visible canvas top-left. World coordinates match `draw()` coordinates in the current hybrid-expanded visible world.

Pointer edge helpers are fixed-update state intended for `update()`. A quick tap between updates can make pressed and released both true for the next update. Last known coordinates remain available after release.

## Engine Viewport

Import:

```code
import everything as Viewport from "engine/viewport.code";
```

API:

| Function | Returns |
| --- | --- |
| `view_left()` | `real` |
| `view_top()` | `real` |
| `view_width()` | `real` |
| `view_height()` | `real` |
| `view_right()` | `real` |
| `view_bottom()` | `real` |
| `safe_left()` | `real` |
| `safe_top()` | `real` |
| `safe_width()` | `real` |
| `safe_height()` | `real` |
| `safe_right()` | `real` |
| `safe_bottom()` | `real` |
| `hud_width()` | `real` |
| `hud_height()` | `real` |

Example:

```code
if x > Viewport.safe_right() then {
  x = Viewport.safe_right();
}
```

## Engine Scene

Import:

```code
import { HudDrawable, Scene, SceneLoop, Updatable, WorldDrawable } from "engine/scene.code";
```

Lifecycle interfaces:

| Interface | Method |
| --- | --- |
| `Startable` | `start()` |
| `Updatable` | `update()` |
| `WorldDrawable` | `draw()` |
| `HudDrawable` | `draw_hud()` |

Scene authoring shape:

```code
object Player {
  integer x;

  constructor() {
    x = 100;
  }

  implement Updatable.update() {
    if key_is_down(39) then x += 2;
  }

  implement WorldDrawable.draw() {
    Draw.rectangle(x, 100, 24, 24, rgb(1, 1, 1));
  }
}
```

Scene methods commonly used by apps:

| Method | Inputs | Behavior |
| --- | --- | --- |
| `add_startable(item)` | `Startable` | stages startable add |
| `remove_startable(item)` | `Startable` | stages startable remove |
| `add_updatable(item)` | `Updatable` | stages update add |
| `remove_updatable(item)` | `Updatable` | stages update remove |
| `add_world_drawable(item, layer)` | `WorldDrawable`, `integer` | stages world draw add |
| `remove_world_drawable(item)` | `WorldDrawable` | stages world draw remove |
| `set_world_draw_layer(item, layer)` | `WorldDrawable`, `integer` | stages layer change |
| `add_hud_drawable(item, layer)` | `HudDrawable`, `integer` | stages HUD draw add |
| `remove_hud_drawable(item)` | `HudDrawable` | stages HUD draw remove |
| `set_hud_draw_layer(item, layer)` | `HudDrawable`, `integer` | stages HUD layer change |

`SceneLoop`:

| Method | Behavior |
| --- | --- |
| `start()` | applies pending registration and starts once |
| `update()` | applies pending changes, then updates all |
| `draw()` | draws all world drawables |
| `draw_hud()` | draws all HUD drawables |

Common mistakes:

- Scene registration changes are staged and applied at the start of the next `update()` phase.
- Draw order is by layer, then registration order.
- `engine.view` and `engine.loop` remain compatibility modules; prefer `engine.viewport` and `engine.scene`.

## Raw Scene Intrinsics

The wrappers above call raw scene intrinsics that are also available:

```text
clear
draw_rectangle
draw_rectangle_outline
draw_line
draw_circle
draw_circle_outline
draw_polygon
draw_polygon_outline
draw_text
draw_image
draw_sprite
key_down
pointer_world_x
pointer_world_y
pointer_screen_x
pointer_screen_y
pointer_is_down
pointer_was_pressed
pointer_was_released
camera_view_left/top/width/height/right/bottom
camera_safe_left/top/width/height/right/bottom
screen_width
screen_height
```

Use wrappers for new app code unless you need to test the host ABI surface directly.
