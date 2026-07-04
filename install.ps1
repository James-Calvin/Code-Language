param(
    [switch]$InstallVsCodeExtension
)

$ErrorActionPreference = "Stop"

$repo = "James-Calvin/Code-Language"
$asset = "code-compiler-win-x64.zip"
$installRoot = Join-Path $HOME ".code-language"
$binDir = Join-Path $installRoot "bin"
$downloadUrl = "https://github.com/$repo/releases/latest/download/$asset"
$zipPath = Join-Path ([System.IO.Path]::GetTempPath()) $asset
$extractDir = Join-Path ([System.IO.Path]::GetTempPath()) ("code-language-install-" + [System.Guid]::NewGuid().ToString("N"))

function Resolve-CodeCommand {
    $codeCommand = Get-Command "code" -ErrorAction SilentlyContinue
    if ($codeCommand) {
        if (-not [string]::IsNullOrWhiteSpace($codeCommand.Source)) {
            return $codeCommand.Source
        }
        if (-not [string]::IsNullOrWhiteSpace($codeCommand.Path)) {
            return $codeCommand.Path
        }
        return $codeCommand.Name
    }

    return $null
}

function Install-VsCodeExtension([string]$VsixPath) {
    $manualCommand = "code --install-extension `"$VsixPath`""
    $codeCommand = Resolve-CodeCommand
    if ([string]::IsNullOrWhiteSpace($codeCommand)) {
        Write-Host "VS Code CLI 'code' was not found. Install the Code language extension manually with:"
        Write-Host "  $manualCommand"
        return
    }

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $codeCommand --install-extension $VsixPath --force 2>&1
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    foreach ($line in $output) {
        Write-Host $line
    }

    if ($exitCode -ne 0) {
        Write-Warning "VS Code extension install failed. Compiler install completed. Manual command: $manualCommand"
    }
}

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
    Write-Host "Added $binDir to your user PATH."
} else {
    Write-Host "$binDir is already on your user PATH."
}

if (-not (($env:Path -split ";") | Where-Object { $_.TrimEnd("\") -ieq $binDir.TrimEnd("\") })) {
    $env:Path = if ([string]::IsNullOrWhiteSpace($env:Path)) { $binDir } else { "$env:Path;$binDir" }
    Write-Host "Added $binDir to this PowerShell session PATH."
}

if ($InstallVsCodeExtension) {
    try {
        $compilerPath = Join-Path $binDir "compiler.exe"
        $installedVersionOutput = & $compilerPath --version
        if ($LASTEXITCODE -ne 0) {
            throw "Could not read installed compiler version."
        }
        $installedVersion = (($installedVersionOutput | Select-Object -First 1).ToString()).Trim()
        $vsixAsset = "code-language-vscode-$installedVersion.vsix"
        $vsixUrl = "https://github.com/$repo/releases/latest/download/$vsixAsset"
        $vsixPath = Join-Path $binDir $vsixAsset
        Write-Host "Downloading $vsixUrl"
        Invoke-WebRequest -Uri $vsixUrl -OutFile $vsixPath
        Install-VsCodeExtension $vsixPath
    } catch {
        Write-Warning "Could not download or install the VS Code extension. Compiler install completed. $($_.Exception.Message)"
    }
}

Write-Host "Installed compiler to $binDir"
Write-Host "Try: compiler --version"
