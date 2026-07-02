param(
    [string]$Configuration = "Release",
    [switch]$SkipTests,
    [switch]$NoAutoIncrementVersion
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$releaseScript = Join-Path $PSScriptRoot "release.ps1"
$artifactDirectory = Join-Path $repoRoot "artifacts\release\code-compiler-win-x64"
$installDirectory = [System.IO.Path]::GetFullPath((Join-Path $HOME ".code-language\bin"))
$projectPath = Join-Path $repoRoot "ConsoleApp1\ConsoleApp1.csproj"
$releaseDocPath = Join-Path $repoRoot "docs\release.md"
$installedCompiler = Join-Path $installDirectory "compiler.exe"

function Get-NextPrereleaseVersion([string]$Version) {
    $match = [regex]::Match($Version, "^(?<prefix>\d+\.\d+\.\d+-[0-9A-Za-z][0-9A-Za-z-]*(?:\.[0-9A-Za-z-]+)*\.)(?<number>\d+)$")
    if (-not $match.Success) {
        throw "Cannot auto-increment compiler version '$Version'. Expected a numeric prerelease suffix such as 0.1.0-alpha.11."
    }

    $number = [int]$match.Groups["number"].Value
    return "$($match.Groups["prefix"].Value)$($number + 1)"
}

function Replace-RequiredText([string]$Path, [string]$OldValue, [string]$NewValue) {
    $content = Get-Content -Path $Path -Raw
    $updated = $content.Replace($OldValue, $NewValue)
    if ($updated -eq $content) {
        throw "Could not update '$Path'; expected text was not found: $OldValue"
    }

    Set-Content -Path $Path -Value $updated -NoNewline
}

function Update-ReleaseVersion([string]$CurrentVersion, [string]$NextVersion) {
    Replace-RequiredText $projectPath "<Version>$CurrentVersion</Version>" "<Version>$NextVersion</Version>"
    Replace-RequiredText $projectPath "<InformationalVersion>$CurrentVersion</InformationalVersion>" "<InformationalVersion>$NextVersion</InformationalVersion>"
    if (Test-Path $releaseDocPath) {
        Replace-RequiredText $releaseDocPath "Current release target: ``$CurrentVersion``." "Current release target: ``$NextVersion``."
        Replace-RequiredText $releaseDocPath "v$CurrentVersion" "v$NextVersion"
    }
}

function Test-PathListContains([string]$PathValue, [string]$ExpectedPath) {
    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $false
    }

    foreach ($part in ($PathValue -split ";")) {
        if ($part.TrimEnd("\") -ieq $ExpectedPath.TrimEnd("\")) {
            return $true
        }
    }

    return $false
}

function Ensure-InstallDirectoryOnUserPath([string]$Directory) {
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if (-not (Test-PathListContains $userPath $Directory)) {
        $newUserPath = if ([string]::IsNullOrWhiteSpace($userPath)) { $Directory } else { "$userPath;$Directory" }
        [Environment]::SetEnvironmentVariable("Path", $newUserPath, "User")
        Write-Host "Added $Directory to your user PATH."
    } else {
        Write-Host "$Directory is already on your user PATH."
    }

    if (-not (Test-PathListContains $env:Path $Directory)) {
        $env:Path = if ([string]::IsNullOrWhiteSpace($env:Path)) { $Directory } else { "$env:Path;$Directory" }
        Write-Host "Added $Directory to this PowerShell session PATH."
    }
}

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
        if ($NoAutoIncrementVersion) {
            throw "Increment the compiler version before the final build/install gate (currently $projectVersion)."
        }

        $nextVersion = Get-NextPrereleaseVersion $projectVersion
        Update-ReleaseVersion $projectVersion $nextVersion
        $projectVersion = $nextVersion
        Write-Host "Auto-incremented compiler version to $projectVersion"
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

Ensure-InstallDirectoryOnUserPath $installDirectory

Write-Host "Installed compiler $installedVersion to $installDirectory"
Write-Host "Verified installed web runtime SHA256: $installedRuntimeHash"
Write-Host "Verified installed Wasm runtime SHA256: $installedWasmRuntimeHash"
