# CLI and Tooling

Commands are run from the repository root.

## Run a `.code` File

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- ConsoleApp1/examples/arithmetic.code
```

Behavior:

- Compiles the source to `.bytecode`.
- Runs it unless `--compile-only` is present.
- Default target is `vm-native`.

## Compile Only

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --compile-only ConsoleApp1/examples/arithmetic.code
```

Custom output:

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --compile-only --out .tmp/demo.bytecode ConsoleApp1/examples/arithmetic.code
```

Common mistakes:

- `--out` points to a bytecode file for normal compile mode.
- `--out` points to a directory for `--build-web`.

## Run Bytecode or Library Artifacts

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- ConsoleApp1/examples/arithmetic.bytecode
```

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- path/to/package-0.1.0-vm-native.codelib
```

The CLI accepts `.bytecode` and `.codelib` inputs.

## Disassemble

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --disasm path/to/file.bytecode
```

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --disasm path/to/library.codelib
```

## Dump Tokens

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --dump-tokens ConsoleApp1/examples/arithmetic.code
```

This prints lexer tokens with line and column information.

## Compile Targets

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --target vm-web --compile-only ConsoleApp1/examples/time.code
```

Targets:

| Target | Use |
| --- | --- |
| `vm-native` | Default CLI/native host bindings |
| `vm-web` | Web host capability checks and web host binding table |

Common mistakes:

- Native-only APIs such as `read_line()` and `sleep_ms()` are rejected for `vm-web`.
- Runtime host mode follows `--target` when running `.code`, `.bytecode`, or `.codelib` through the CLI.

## Build Web

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --build-web ConsoleApp1/examples/web_scene.code
```

Custom output:

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --build-web --out .tmp/web-build ConsoleApp1/examples/web_scene.code
```

Behavior:

- Forces target `vm-web`.
- Emits `index.html` with embedded bytecode.
- Copies `assets/` when present.
- Use `--emit-web-bytecode` with `--build-web` to also write `app.bytecode` for debugging or inspection.

Common mistakes:

- `--build-web` does not combine with module graph output yet.
- The entry module must export a valid `MainScene` object.

## Module Graph

Print graph:

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --compile-only --dump-module-graph ConsoleApp1/examples/modules/main.code
```

Write graph:

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --compile-only --dump-module-graph graph.json ConsoleApp1/examples/modules/main.code
```

Force format:

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --compile-only --dump-module-graph graph.txt --module-graph-format dot ConsoleApp1/examples/modules/main.code
```

Formats:

| Format | How selected |
| --- | --- |
| `text` | default |
| `json` | `.json` output or `--module-graph-format json` |
| `dot` | `.dot`, `.gv`, or `--module-graph-format dot` |

## Linker Trace

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --compile-only --trace-linker ConsoleApp1/examples/modules/main.code
```

This prints import resolution and linker steps to stderr.

## Tests

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --run-tests
```

Coverage includes:

- VM opcode tests
- compiler integration tests
- examples from the example catalog
- web build smoke tests
- native/web host ABI parity checks
- fuzz suites for arithmetic, booleans, strings, loops, and panic

If the CLI is run with no args and without `--skip-tests`, it runs tests and then a small bytecode demo.
