# Code

Code is an experimental, code-first programming language for building 2D interactive applications and games for the web.

The current release ships a compiler CLI named `compiler`. The default workflow is:

```powershell
compiler source.code
```

That builds a deployable static web app in a folder named `source/` in your current directory. Open `source/index.html` directly or upload the folder to any static web host.

## Install

Windows:

```powershell
irm https://raw.githubusercontent.com/James-Calvin/Code-Language/master/install.ps1 | iex
```

macOS/Linux:

```sh
curl -fsSL https://raw.githubusercontent.com/James-Calvin/Code-Language/master/install.sh | sh
```

Manual downloads are available from the [GitHub Releases page](https://github.com/James-Calvin/Code-Language/releases). Release zips include the compiler executable plus sidecar `lib/` engine modules and `web-runtime/` browser files needed for web builds. Extract the whole folder and put that folder on `PATH`; do not move only the executable. Use the release `SHA256SUMS.txt` file to verify manual downloads.

## Build A Web App

Default output folder:

```powershell
compiler ConsoleApp1/examples/shape_dodge.code
```

This creates `shape_dodge/index.html` in the current directory.

Custom output folder:

```powershell
compiler ConsoleApp1/examples/shape_dodge.code -o MyGame
```

This creates `MyGame/index.html`.

The web app output includes:

- `index.html` with embedded bytecode and browser runtime
- `code-runtime.wasm`, loaded as a cacheable sidecar when served and embedded as a direct-`file://` fallback
- copied `assets/` content when present beside the entry file or package root
- a full-window canvas runtime with keyboard, primary pointer, drawing, image/sprite, audio, and diagnostics support

## Native Mode

Native mode is useful for console examples and compiler development:

```powershell
compiler --native ConsoleApp1/examples/arithmetic.code
```

The default user workflow is web output. Use `--native` only when you specifically want native host bindings.

## Smallest Program

```code
function start() {
}

function update() {
}

function draw() {
  Draw.clearScreen(Colors.rgb(0, 0, 0));
  Draw.text("hello, world", 24, 24, 18, "left", "top", Colors.rgb(255, 255, 255));
}
```

Save this as `hello.code`, run `compiler hello.code`, then open `hello/index.html`.

## Language Snapshot

Implemented today:

- typed variables/functions; primitives: `integer`, `whole`, `real`, `boolean`, `string`, sized numeric boundary types, and `byte`
- same-module global variables/constants plus built-in real constants `pi` and `tau`
- objects, records, interfaces, arrays, maps, sets, queues, stacks, optionals, and typed recoverable `fallible` errors
- `if`, `switch`, `while`, `for`, `foreach`, `break`, `continue`, functions, methods, modules, package manifests, lockfiles, and library artifacts
- web app lifecycle entry through top-level `start()`, `update()`, `draw()`, and optional `drawHud()`
- engine wrapper modules for colors, drawing, input, viewport, diagnostics, runtime scheduling, audio, and scene composition
- public naming style uses `PascalCase` for types/namespaces and `camelCase` for functions, methods, fields, locals, constants, and lifecycle hooks

See the [language reference](docs/reference/README.md), [naming and style guide](docs/reference/naming-and-style.md), [web app runtime contract](docs/web-app-v1.md), and [example catalog](docs/example-catalog.md) for the implementation-truth docs.

## Runtime profiling and benchmarks

Append `?code-profile=1` to a generated app URL to collect VM instruction,
function, host-call, allocation, and stack metrics. In browser developer tools,
use `await CodeRuntime.profile.report()` for console tables or
`await CodeRuntime.profile.json()` for exportable JSON. Worker-backed profiling
methods return promises. Profiling is disabled by default.

Maintainers run deterministic release-mode VM benchmarks with:

```powershell
node scripts/benchmark-runtime.mjs
```

See [benchmarks/README.md](benchmarks/README.md) for baseline comparison rules.

Generated web apps use bytecode v10 and run the Rust/Wasm VM, lifecycle methods,
and updates in a dedicated worker. Fixed 60 Hz updates are the default. Drawing is
encoded by the worker, replayed on the main-thread canvas, and remains limited
by browser/display refresh. `Runtime.useContinuousUpdates()` is an explicit
opt-in for applications designed around measured update deltas.

## For Maintainers

Maintainer build, test, and release commands live in [docs/release.md](docs/release.md).
