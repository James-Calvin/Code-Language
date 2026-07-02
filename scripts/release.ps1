param(
    [string]$Version = "",
    [string]$Configuration = "Release",
    [string]$OutputRoot = "artifacts/release",
    [string[]]$Runtimes = @("win-x64", "linux-x64", "osx-x64", "osx-arm64"),
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "ConsoleApp1/ConsoleApp1.csproj"
$outputRootPath = Join-Path $repoRoot $OutputRoot
$wasmRuntimePath = Join-Path $repoRoot "runtime-wasm"

$rustSetupMessage = @"
Rust/Wasm release tooling is required.

Install on Windows:
  winget install --id Rustlang.Rustup -e
  winget install --id Microsoft.VisualStudio.2022.BuildTools -e
  Restart PowerShell so PATH includes cargo, rustc, and rustup.
  rustup toolchain install 1.83.0 --profile minimal --target wasm32-unknown-unknown
  If cargo test still cannot find link.exe, launch "x64 Native Tools Command Prompt for VS 2022"
  or install the Visual Studio Build Tools "Desktop development with C++" workload.

Release builds compile runtime-wasm with:
  cargo build --manifest-path runtime-wasm/Cargo.toml --release --target wasm32-unknown-unknown --locked
"@

$nodeSetupMessage = @"
Node.js is required for release browser compatibility tests.

Install Node.js LTS, restart your shell so PATH includes node, and ensure a local
Chromium-family browser such as Chrome, Edge, or Chromium is installed.
"@

function Require-CommandOnPath([string]$Name, [string]$SetupMessage = $rustSetupMessage) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Missing required command '$Name'.`n$SetupMessage"
    }
}

function Invoke-CheckedNative([string]$FailureMessage, [string]$FilePath, [string[]]$ArgumentList) {
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $FilePath @ArgumentList 2>&1
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    foreach ($line in $output) {
        Write-Host $line
    }

    if ($exitCode -ne 0) {
        throw $FailureMessage
    }
}

function Compress-ArchiveWithRetry([string]$SourcePath, [string]$DestinationPath) {
    $lastError = $null
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Compress-Archive -Path $SourcePath -DestinationPath $DestinationPath
            return
        } catch {
            $lastError = $_
            if ($attempt -eq 5) {
                break
            }

            Start-Sleep -Milliseconds (250 * $attempt)
        }
    }

    throw $lastError
}

function Resolve-NodeCommand {
    $nodeCommand = Get-Command "node" -ErrorAction SilentlyContinue
    if ($nodeCommand) {
        if (-not [string]::IsNullOrWhiteSpace($nodeCommand.Source)) {
            return $nodeCommand.Source
        }
        if (-not [string]::IsNullOrWhiteSpace($nodeCommand.Path)) {
            return $nodeCommand.Path
        }
        return $nodeCommand.Name
    }

    $nodeCandidates = @(
        (Join-Path $env:ProgramFiles "nodejs\node.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "nodejs\node.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\nodejs\node.exe")
    )

    foreach ($candidate in $nodeCandidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    throw "Missing required command 'node'.`n$nodeSetupMessage"
}

function Find-VcVars64 {
    $candidates = New-Object System.Collections.Generic.List[string]
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $installations = & $vswhere -all -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
        if ($LASTEXITCODE -ne 0 -or $installations.Count -eq 0) {
            $installations = & $vswhere -all -products * -property installationPath 2>$null
        }

        foreach ($installation in $installations) {
            if (-not [string]::IsNullOrWhiteSpace($installation)) {
                $candidates.Add((Join-Path $installation "VC\Auxiliary\Build\vcvars64.bat"))
            }
        }
    }

    $candidates.Add("C:\Program Files\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat")
    $candidates.Add("C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat")
    $candidates.Add("C:\Program Files\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat")
    $candidates.Add("C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat")

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

function Invoke-RustRuntimeTests([string]$ManifestPath) {
    if (($IsWindows -or $env:OS -eq "Windows_NT") -and -not (Get-Command "link.exe" -ErrorAction SilentlyContinue)) {
        $vcvars = Find-VcVars64
        if ([string]::IsNullOrWhiteSpace($vcvars)) {
            throw "Missing required command 'link.exe' and could not locate vcvars64.bat.`n$rustSetupMessage"
        }

        Write-Host "Using MSVC environment from $vcvars"
        $commandLine = "call `"$vcvars`" >nul && cargo test --locked --manifest-path `"$ManifestPath`""
        Invoke-CheckedNative "Rust/Wasm runtime tests failed." "cmd.exe" @("/d", "/s", "/c", $commandLine)
        return
    }

    Invoke-CheckedNative "Rust/Wasm runtime tests failed." "cargo" @("test", "--locked", "--manifest-path", $ManifestPath)
}

function Require-RustWasmToolchain {
    Require-CommandOnPath "cargo"
    Require-CommandOnPath "rustc"
    Require-CommandOnPath "rustup"

    Push-Location $wasmRuntimePath
    try {
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            $installedTargets = & rustup target list --installed 2>&1
            $rustupExitCode = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
        if ($rustupExitCode -ne 0) {
            throw "Could not list installed Rust targets.`n$installedTargets`n$rustSetupMessage"
        }

        if ($installedTargets -notcontains "wasm32-unknown-unknown") {
            throw "Missing required Rust target 'wasm32-unknown-unknown'.`n$rustSetupMessage"
        }
    } finally {
        Pop-Location
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$project = Get-Content $projectPath
    $Version = $project.Project.PropertyGroup.Version
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version was not provided and ConsoleApp1.csproj does not define <Version>."
}

$env:DOTNET_CLI_HOME = Join-Path $repoRoot ".dotnet"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_NOLOGO = "1"
$env:NUGET_PACKAGES = Join-Path $repoRoot ".nuget"
$env:TEMP = Join-Path $repoRoot ".tmp"
$env:TMP = Join-Path $repoRoot ".tmp"

New-Item -ItemType Directory -Force $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:TEMP, $outputRootPath | Out-Null

Require-RustWasmToolchain

if (-not $SkipTests) {
    $nodeCommand = Resolve-NodeCommand

    $wasmManifestPath = Join-Path $wasmRuntimePath "Cargo.toml"
    Invoke-RustRuntimeTests $wasmManifestPath

    dotnet run --project $projectPath -c $Configuration -- --run-tests
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed."
    }

    Invoke-CheckedNative "Browser compatibility tests failed." $nodeCommand @((Join-Path $repoRoot "scripts/test-browser-compat.mjs"))
}

$checksumLines = New-Object System.Collections.Generic.List[string]

foreach ($runtime in $Runtimes) {
    $artifactName = "code-compiler-$runtime"
    $publishDir = Join-Path $outputRootPath $artifactName
    $zipPath = Join-Path $outputRootPath "$artifactName.zip"

    if (Test-Path $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }
    if (Test-Path $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    dotnet publish $projectPath `
        -c $Configuration `
        -r $runtime `
        --self-contained true `
        -o $publishDir `
        -p:Version=$Version `
        -p:InformationalVersion=$Version `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for runtime '$runtime'."
    }

    $requiredSidecarFiles = @(
        "lib/engine/colors.code",
        "lib/engine/drawing.code",
        "lib/engine/input.code",
        "lib/engine/viewport.code",
        "web-runtime/code-vm-web.js",
        "web-runtime/code-runtime.wasm"
    )
    foreach ($requiredSidecarFile in $requiredSidecarFiles) {
        $requiredPath = Join-Path $publishDir $requiredSidecarFile
        if (-not (Test-Path $requiredPath)) {
            throw "Publish output for runtime '$runtime' is missing required sidecar file '$requiredSidecarFile'."
        }
    }

    Compress-ArchiveWithRetry (Join-Path $publishDir "*") $zipPath

    $hash = Get-FileHash -Algorithm SHA256 -Path $zipPath
    $checksumLines.Add("$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $zipPath)")

    Write-Host "Published $artifactName"
    Write-Host "Archive: $zipPath"
}

$checksumPath = Join-Path $outputRootPath "SHA256SUMS.txt"
Set-Content -Path $checksumPath -Value $checksumLines -Encoding ascii
Write-Host "Checksums: $checksumPath"
