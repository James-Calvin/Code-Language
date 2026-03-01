# Code Web Runtime Harness

This folder contains a first browser runtime harness for Code bytecode:
- `code-vm-web.js`: JavaScript VM + web host ABI bindings
- `index.html`: load and run `.bytecode` or `.codelib` files in a browser

## Supported host ABI (web)
- `std.io.print`
- `std.time.unix_ms`
- `std.time.unix_us`
- `std.time.mono_ns`
- `std.time.mono_ticks`
- `std.time.mono_ticks_per_second`

Native-only host calls are present with explicit runtime diagnostics:
- `std.io.read_line` -> `HostBindingError` on `vm-web`
- `std.time.sleep_ms` -> `HostBindingError` on `vm-web`

Engine ABI stubs are available as no-ops on web:
- `engine.window.create`
- `engine.window.should_close`
- `engine.window.present`
- `engine.input.key_down`
- `engine.gfx.clear`
- `engine.gfx.draw_rect`

## Quick start
1. Compile a program for web:
   - `dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --target vm-web --compile-only ConsoleApp1/examples/host_abi_basic.code`
2. Serve this repo root as static files (example):
   - `python -m http.server 8000`
3. Open `http://localhost:8000/web-runtime/index.html`
4. Load the generated `.bytecode` file and click **Run**.
