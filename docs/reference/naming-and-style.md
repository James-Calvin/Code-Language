# Naming and Style

Code source should read consistently in examples, libraries, and user apps.

## Casing Rules

| Symbol kind | Style | Examples |
| --- | --- | --- |
| Objects, records, interfaces, enums, enum members | `PascalCase` | `Player`, `SceneLoop`, `WorldDrawable`, `ParseError.MissingFile` |
| Namespace aliases | `PascalCase` | `Draw`, `Input`, `Viewport`, `Colors`, `Diagnostics`, `Audio` |
| Functions and methods | `camelCase` | `drawHud`, `keyIsDown`, `removeAt`, `playSound` |
| Fields, locals, parameters, and constants | `camelCase` | `playerSpeed`, `frameCount`, `safeWidth` |
| Import paths and package paths | lowercase file paths | `"engine/drawing.code"` |

## Acronyms

Treat acronyms as ordinary words inside identifiers:

```code
function drawHud() {
  Draw.text("score", Viewport.hudWidth() - 16, 16, 14, "right", "top", Colors.rgb(255, 255, 255));
}
```

Accepted compact domain terms may stay lowercase when that is the clearest form. Current examples are `rgb` and `rgba`.

## Public API Policy

- Public language and engine APIs should use `PascalCase` or `camelCase`.
- Avoid `snake_case` in source-level APIs and examples.
- Lower-level host ABI symbols may retain historical names internally; they are not the preferred authoring surface.
- Wrapper namespaces in `lib/engine/` are the recommended way to write app code.
