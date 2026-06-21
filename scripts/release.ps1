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

if (-not $SkipTests) {
    dotnet run --project $projectPath -c $Configuration -- --run-tests
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed."
    }
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

    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath

    $hash = Get-FileHash -Algorithm SHA256 -Path $zipPath
    $checksumLines.Add("$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $zipPath)")

    Write-Host "Published $artifactName"
    Write-Host "Archive: $zipPath"
}

$checksumPath = Join-Path $outputRootPath "SHA256SUMS.txt"
Set-Content -Path $checksumPath -Value $checksumLines -Encoding ascii
Write-Host "Checksums: $checksumPath"
