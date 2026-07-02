# Release Process

Current release target: `0.1.0-alpha.15`.

This is a maintainer-facing document. User install and quickstart instructions live in the README.

## Public Install Paths

Primary alpha install:

- Windows: `irm https://raw.githubusercontent.com/James-Calvin/Code-Language/master/install.ps1 | iex`
- macOS/Linux: `curl -fsSL https://raw.githubusercontent.com/James-Calvin/Code-Language/master/install.sh | sh`

Manual install:

1. Download the matching zip from `https://github.com/James-Calvin/Code-Language/releases`.
2. Verify the zip against `SHA256SUMS.txt`.
3. Extract the whole folder and put the extracted folder on `PATH`; keep the compiler executable beside the bundled `lib/` and `web-runtime/` folders.

Package managers are follow-up work after the alpha CLI stabilizes. Preferred order: winget, Homebrew tap, Scoop, optional .NET global tool.

## Build And Test

From the repository root:

```powershell
dotnet build ConsoleApp1.sln -c Release
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -c Release -- --run-tests
node scripts/benchmark-runtime.mjs
node scripts/benchmark-scheduler.mjs
node scripts/test-web-vm.mjs
node scripts/test-rust-wasm.mjs
node scripts/benchmark-rust-wasm.mjs
node scripts/test-direct-wasm.mjs
node scripts/benchmark-direct-wasm.mjs
node scripts/test-generated-worker.mjs
node scripts/test-browser-compat.mjs
```

Release builds require Rust 1.83 with the `wasm32-unknown-unknown` target. The
release script checks for `cargo`, `rustc`, `rustup`, and the Wasm target before
running .NET so missing Rust tooling fails with setup guidance instead of an
MSBuild `9009` command-not-found error.
Full release tests also require Node.js and at least one local Chromium-family
browser such as Chrome, Edge, or Chromium for the browser compatibility suite.
If `node` is not visible in the current shell, the release script also checks
common Windows Node.js install paths under `Program Files`, which helps when
VS Code was opened before PATH updates were applied.

Windows setup:

```powershell
winget install --id Rustlang.Rustup -e
winget install --id Microsoft.VisualStudio.2022.BuildTools -e
# Restart PowerShell so PATH includes cargo, rustc, and rustup.
rustup toolchain install 1.83.0 --profile minimal --target wasm32-unknown-unknown
```

`cargo test` uses the Windows MSVC host toolchain and needs `link.exe`. Release
packaging with `-SkipTests` can build the Wasm runtime without `link.exe`. For
the full validation path, `scripts/release.ps1` first uses `link.exe` from
`PATH`; if it is missing, the script locates Visual Studio Build Tools and runs
Rust tests through `vcvars64.bat`. If that auto-detection fails, run release from
the "x64 Native Tools Command Prompt for VS 2022" or install the Build Tools
"Desktop development with C++" workload.

From the repository root, the manual Rust runtime build command is:

```powershell
cargo build --manifest-path runtime-wasm/Cargo.toml --release --target wasm32-unknown-unknown --locked
```

MSBuild builds the locked dependency-free runtime and packages `web-runtime/code-runtime.wasm`.
`scripts/release.ps1` also runs `cargo test --locked` for `runtime-wasm` before
the .NET harness, then runs the direct-Wasm browser compatibility suite. The
Rust/Wasm GC regression tests cover dequeued queue values and free-slot reuse so
memory leaks stay release-gated.
`--run-tests` includes the arithmetic, boolean, string, loop, and panic fuzz
suites. Runtime changes also pass executable C#/JavaScript VM conformance, the
profiler smoke checks embedded in the benchmark runner, and record benchmark
results before release. Bytecode/runtime changes must also cover malformed v10
metadata and explicit v9 rejection. Browser scheduling or direct-Wasm runtime
changes require current generated-worker smoke tests plus
`node scripts/test-browser-compat.mjs`. The compatibility suite automates local
Chromium-family desktop browsers and writes a mobile/manual report page for iOS
Safari, iOS Chrome, Android Chrome, Android Firefox, Android Edge, and Samsung
Internet checks before shipping a runtime release.

## Local Pass Completion Gate

Every performance or feature pass ends by packaging and installing the current
Windows Release compiler after code, tests, benchmarks, and documentation are
complete:

```powershell
./scripts/install-local.ps1 -SkipTests
```

Omit `-SkipTests` if the full harness has not already passed during the same
pass. If the installed compiler version already matches the project version, the
script automatically increments numeric prerelease versions such as
`0.1.0-alpha.11` to `0.1.0-alpha.12`, updates the project metadata, and updates
this release document. Pass `-NoAutoIncrementVersion` to restore the strict
"fail unless manually incremented" behavior. The script builds the Windows
release artifact, installs it to `$HOME/.code-language/bin`, verifies
`compiler --version`, and checks that the installed JavaScript and Wasm runtime
hashes match the working tree. It also ensures `$HOME/.code-language/bin` is on
the user PATH and adds it to the current PowerShell session when possible.
Smoke-test at least one native program and one generated web app with the
installed executable when the pass changes compiler or runtime behavior.
Release archive creation retries short-lived file locks around freshly
published executables, which can happen in synced directories such as OneDrive
or while antivirus scanners inspect the output.

Set `CODE_COMPILER` to the installed executable when running generated-worker
browser gates against the installed package:

```powershell
$env:CODE_COMPILER = "$HOME/.code-language/bin/compiler.exe"
node scripts/test-generated-worker.mjs
node scripts/test-browser-compat.mjs --keep
```

## Create Release Artifacts

```powershell
./scripts/release.ps1
```

The script builds self-contained single-file runtime zips for:

- `code-compiler-win-x64.zip`
- `code-compiler-linux-x64.zip`
- `code-compiler-osx-x64.zip`
- `code-compiler-osx-arm64.zip`
- `SHA256SUMS.txt`

Artifacts are written to `artifacts/release/`, which is intentionally git-ignored.

To build a subset:

```powershell
./scripts/release.ps1 -Runtimes win-x64
```

## Smoke Test A Release Folder

From the repository root, with an extracted release folder outside the project being tested:

```powershell
$release = Resolve-Path artifacts/release/code-compiler-win-x64
$compiler = Join-Path $release "compiler.exe"
New-Item -ItemType Directory -Force .tmp/release-smoke | Out-Null
Push-Location .tmp/release-smoke
& $compiler --version
& $compiler --help
& $compiler ../../ConsoleApp1/examples/shape_dodge.code -o web
& $compiler --native ../../ConsoleApp1/examples/arithmetic.code
Pop-Location
```

Checks:

- `--help` does not list maintainer-only flags such as `--run-tests`.
- Web build output contains `index.html`.
- Web build succeeds from a different working directory, proving imports can resolve the bundled `lib/` and `web-runtime/` beside the installed compiler.

## GitHub Release Checklist

1. Run the build and full test harness.
2. Run benchmarks and browser smoke tests required by the changed area.
3. Update related documentation.
4. Run `./scripts/install-local.ps1 -SkipTests` as the final local pass gate; it auto-increments the prerelease version if needed.
5. Run `./scripts/release.ps1` for all release runtimes.
6. Smoke test at least the Windows zip locally.
7. Commit and tag, for example `v0.1.0-alpha.15`.
8. Create a GitHub prerelease.
9. Upload all `code-compiler-*.zip` files and `SHA256SUMS.txt`.
10. Test `install.ps1` from the GitHub release before announcing.
