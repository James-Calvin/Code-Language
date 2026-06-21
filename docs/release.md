# Release Process

Current release target: `0.1.0-alpha.8`.

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
node scripts/test-generated-worker.mjs
```

`--run-tests` includes the arithmetic, boolean, string, loop, and panic fuzz
suites. Runtime changes also pass executable C#/JavaScript VM conformance, the
profiler smoke checks embedded in the benchmark runner, and record benchmark
results before release. Bytecode/runtime changes must also cover malformed v10
metadata and explicit v9 rejection. Browser scheduling changes require current
Chrome and Edge direct-`file://` generated-worker smoke tests; Firefox and Safari are checked before
shipping a runtime release.

## Local Pass Completion Gate

Every performance or feature pass increments the compiler version, then ends
by packaging and installing the current Windows Release compiler after code,
tests, benchmarks, and documentation are complete:

```powershell
./scripts/install-local.ps1 -SkipTests
```

Omit `-SkipTests` if the full harness has not already passed during the same
pass. The script rejects an unchanged installed version, builds the Windows release artifact, installs it to
`$HOME/.code-language/bin`, verifies `compiler --version`, and checks that the
installed web runtime hash matches the working tree. Smoke-test at least one
native program and one generated web app with the installed executable when
the pass changes compiler or runtime behavior.

Set `CODE_COMPILER` to the installed executable when running generated-worker
browser gates against the installed package:

```powershell
$env:CODE_COMPILER = "$HOME/.code-language/bin/compiler.exe"
node scripts/test-generated-worker.mjs
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

1. Update `ConsoleApp1/ConsoleApp1.csproj` version fields.
2. Run the build and full test harness.
3. Run benchmarks and browser smoke tests required by the changed area.
4. Update related documentation.
5. Run `./scripts/install-local.ps1 -SkipTests` as the final local pass gate.
6. Run `./scripts/release.ps1` for all release runtimes.
7. Smoke test at least the Windows zip locally.
8. Commit and tag, for example `v0.1.0-alpha.8`.
9. Create a GitHub prerelease.
10. Upload all `code-compiler-*.zip` files and `SHA256SUMS.txt`.
11. Test `install.ps1` from the GitHub release before announcing.
