# Release Process

Current release target: `0.1.0-alpha.5`.

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
node scripts/test-web-vm.mjs
```

`--run-tests` includes the arithmetic, boolean, string, loop, and panic fuzz
suites. Runtime changes also pass executable C#/JavaScript VM conformance, the
profiler smoke checks embedded in the benchmark runner, and record benchmark
results before release.

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
3. Run `./scripts/release.ps1`.
4. Smoke test at least the Windows zip locally.
5. Commit and tag, for example `v0.1.0-alpha.3`.
6. Create a GitHub prerelease.
7. Upload all `code-compiler-*.zip` files and `SHA256SUMS.txt`.
8. Test `install.ps1` from the GitHub release before announcing.
