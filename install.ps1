$ErrorActionPreference = "Stop"

$repo = "James-Calvin/Code-Language"
$asset = "code-compiler-win-x64.zip"
$installRoot = Join-Path $HOME ".code-language"
$binDir = Join-Path $installRoot "bin"
$downloadUrl = "https://github.com/$repo/releases/latest/download/$asset"
$zipPath = Join-Path ([System.IO.Path]::GetTempPath()) $asset
$extractDir = Join-Path ([System.IO.Path]::GetTempPath()) ("code-language-install-" + [System.Guid]::NewGuid().ToString("N"))

New-Item -ItemType Directory -Force $binDir, $extractDir | Out-Null

Write-Host "Downloading $downloadUrl"
Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath

Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force
Copy-Item -Path (Join-Path $extractDir "*") -Destination $binDir -Recurse -Force

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
$pathParts = @()
if (-not [string]::IsNullOrWhiteSpace($userPath)) {
    $pathParts = $userPath -split ";"
}

$alreadyOnPath = $pathParts | Where-Object {
    $_.TrimEnd("\") -ieq $binDir.TrimEnd("\")
}

if (-not $alreadyOnPath) {
    $newPath = if ([string]::IsNullOrWhiteSpace($userPath)) { $binDir } else { "$userPath;$binDir" }
    [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    Write-Host "Added $binDir to your user PATH. Open a new terminal before running compiler."
} else {
    Write-Host "$binDir is already on your user PATH."
}

Write-Host "Installed compiler to $binDir"
Write-Host "Try: compiler --version"
