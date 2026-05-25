# Release Process

Current release target: `0.1.0-alpha.1`.

This is a maintainer-facing document. User install and quickstart instructions live in the README.

## Public Install Paths

Primary alpha install:

- Windows: `irm https://raw.githubusercontent.com/James-Calvin/Code-Language/master/install.ps1 | iex`
- macOS/Linux: `curl -fsSL https://raw.githubusercontent.com/James-Calvin/Code-Language/master/install.sh | sh`

Manual install:

1. Download the matching zip from `https://github.com/James-Calvin/Code-Language/releases`.
2. Verify the zip against `SHA256SUMS.txt`.
3. Extract it and put the extracted folder on `PATH`.

Package managers are follow-up work after the alpha CLI stabilizes. Preferred order: winget, Homebrew tap, Scoop, optional .NET global tool.

## Build And Test

From the repository root:

```powershell
dotnet build ConsoleApp1.sln -c Release
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -c Release -- --run-tests
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

From inside an extracted release folder:

```powershell
./compiler --version
./compiler --help
./compiler ../../../../ConsoleApp1/examples/shape_dodge.code -o ../../../../.tmp/release-smoke/web
./compiler --native ../../../../ConsoleApp1/examples/arithmetic.code
```

Checks:

- `--help` does not list maintainer-only flags such as `--run-tests`.
- Web build output contains `index.html`.
- Web build succeeds from the published folder, proving bundled `lib/` and `web-runtime/` are present.

## GitHub Release Checklist

1. Update `ConsoleApp1/ConsoleApp1.csproj` version fields.
2. Run the build and full test harness.
3. Run `./scripts/release.ps1`.
4. Smoke test at least the Windows zip locally.
5. Commit and tag, for example `v0.1.0-alpha.1`.
6. Create a GitHub prerelease.
7. Upload all `code-compiler-*.zip` files and `SHA256SUMS.txt`.
8. Test `install.ps1` from the GitHub release before announcing.
