# CLI and Tooling

This page documents the installed public compiler CLI. Maintainer-only commands are listed at the end.

## Build A Web App

```powershell
compiler path/to/source.code
```

Behavior:

- Builds a deployable static web app.
- Writes output to `./source/` in the current directory.
- Emits `index.html` with embedded bytecode and browser runtime.
- Copies `assets/` when present beside the entry file or package root.

Custom output:

```powershell
compiler path/to/source.code -o MyApp
```

Equivalent long form:

```powershell
compiler path/to/source.code --output MyApp
```

Common mistakes:

- Output is relative to the current directory, not the entry file directory.
- The entry module must provide either an explicit `MainScene` object or top-level `start()` / `update()` / `draw()` functions.
- Rebuilding overwrites generated files, but it does not clean unrelated files from the output folder.

## Native Mode

```powershell
compiler --native ConsoleApp1/examples/arithmetic.code
```

Behavior:

- Compiles the source to bytecode.
- Writes default bytecode to `./arithmetic.bytecode` in the current directory for the example above.
- Runs it with native host bindings.
- Use this for console examples and native-only APIs such as `readLine()`.

Compile only in native mode:

```powershell
compiler --native --compile-only -o .tmp/demo.bytecode ConsoleApp1/examples/arithmetic.code
```

## Public Options

| Option | Use |
| --- | --- |
| `-o <folder>` / `--output <folder>` | Set web output folder, or native bytecode file when combined with `--native --compile-only` |
| `--native` | Compile and run using native host bindings |
| `--version` | Print compiler version |
| `--help` / `-h` | Print public help |

## Advanced And Maintainer Commands

These flags remain supported for compatibility and compiler development, but they are not part of the public quickstart.

| Command | Use |
| --- | --- |
| `--build-web` | Explicitly request the web build path; public CLI defaults to this for `.code` input |
| `--out <path>` | Legacy alias for `-o` / `--output` |
| `--target vm-native\|vm-web` | Select bytecode compile/run target for internal target checks |
| `--compile-only` | Compile bytecode without running in native/internal modes |
| `--emit-web-bytecode` | Also emit `app.bytecode` when building a web app |
| `--web-backend wasm-vm\|direct-wasm` | Select the generated-web backend; direct Wasm is a gated maintainer preview |
| `--disasm <file.bytecode\|file.codelib>` | Disassemble bytecode or library artifacts |
| `--dump-tokens <file.code>` | Print lexer tokens |
| `--dump-module-graph [out]` | Print or write the module graph |
| `--module-graph-format text\|json\|dot` | Force module graph output format |
| `--trace-linker` | Print linker resolution steps to stderr |
| `--run-tests` | Run the compiler/runtime harness |
| `--skip-tests` | Compatibility no-op for the old no-argument CLI path |

Examples:

```powershell
compiler --target vm-web --compile-only ConsoleApp1/examples/time.code
compiler --compile-only --dump-module-graph graph.json ConsoleApp1/examples/modules/main.code
compiler --disasm path/to/file.bytecode
```
