param(
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$releaseScript = Join-Path $PSScriptRoot "release.ps1"
$artifactDirectory = Join-Path $repoRoot "artifacts\release\code-compiler-win-x64"
$installDirectory = [System.IO.Path]::GetFullPath((Join-Path $HOME ".code-language\bin"))
$projectPath = Join-Path $repoRoot "ConsoleApp1\ConsoleApp1.csproj"
$installedCompiler = Join-Path $installDirectory "compiler.exe"

[xml]$project = Get-Content $projectPath
$projectVersion = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($projectVersion)) {
    throw "ConsoleApp1.csproj does not define a release version."
}
if (Test-Path $installedCompiler) {
    $installedVersion = [string](& $installedCompiler --version)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read the currently installed compiler version."
    }
    if ($installedVersion.Trim() -eq $projectVersion.Trim()) {
        throw "Increment the compiler version before the final build/install gate (currently $projectVersion)."
    }
}

if ($SkipTests) {
    & $releaseScript -Configuration $Configuration -Runtimes @("win-x64") -SkipTests
} else {
    & $releaseScript -Configuration $Configuration -Runtimes @("win-x64")
}
if ($LASTEXITCODE -ne 0) {
    throw "Local compiler release build failed."
}

$resolvedArtifact = (Resolve-Path $artifactDirectory).Path
New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null
Copy-Item -Path (Join-Path $resolvedArtifact "*") -Destination $installDirectory -Recurse -Force

$sourceRuntime = Join-Path $repoRoot "web-runtime\code-vm-web.js"
$installedRuntime = Join-Path $installDirectory "web-runtime\code-vm-web.js"
$sourceWasmRuntime = Join-Path $repoRoot "web-runtime\code-runtime.wasm"
$installedWasmRuntime = Join-Path $installDirectory "web-runtime\code-runtime.wasm"
if (-not (Test-Path $installedCompiler) -or -not (Test-Path $installedRuntime) -or -not (Test-Path $installedWasmRuntime)) {
    throw "The local compiler installation is incomplete."
}

$sourceRuntimeHash = (Get-FileHash -Algorithm SHA256 -Path $sourceRuntime).Hash
$installedRuntimeHash = (Get-FileHash -Algorithm SHA256 -Path $installedRuntime).Hash
if ($sourceRuntimeHash -ne $installedRuntimeHash) {
    throw "The installed web runtime does not match the working tree."
}
$sourceWasmRuntimeHash = (Get-FileHash -Algorithm SHA256 -Path $sourceWasmRuntime).Hash
$installedWasmRuntimeHash = (Get-FileHash -Algorithm SHA256 -Path $installedWasmRuntime).Hash
if ($sourceWasmRuntimeHash -ne $installedWasmRuntimeHash) {
    throw "The installed Wasm runtime does not match the working tree."
}

$installedVersion = & $installedCompiler --version
if ($LASTEXITCODE -ne 0) {
    throw "The installed compiler version check failed."
}

Write-Host "Installed compiler $installedVersion to $installDirectory"
Write-Host "Verified installed web runtime SHA256: $installedRuntimeHash"
Write-Host "Verified installed Wasm runtime SHA256: $installedWasmRuntimeHash"
