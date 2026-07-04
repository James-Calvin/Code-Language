param(
    [string]$Version = "",
    [string]$OutputRoot = "artifacts/release"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repoRoot "ConsoleApp1\ConsoleApp1.csproj"
$outputRootPath = Join-Path $repoRoot $OutputRoot
$sourceExtensionPath = Join-Path $repoRoot "editor\vscode"
$sourceGrammarPath = Join-Path $repoRoot "editor\textmate\code.tmLanguage.json"
$sourceReadmePath = Join-Path $repoRoot "editor\README.md"

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$project = Get-Content $projectPath
    $Version = [string]$project.Project.PropertyGroup.Version
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version was not provided and ConsoleApp1.csproj does not define <Version>."
}

if (-not (Test-Path $sourceGrammarPath)) {
    throw "Missing TextMate grammar: $sourceGrammarPath"
}
if (-not (Test-Path (Join-Path $sourceExtensionPath "package.json"))) {
    throw "Missing VS Code extension package.json."
}

function Escape-Xml([string]$Value) {
    return [System.Security.SecurityElement]::Escape($Value)
}

function Write-Utf8NoBom([string]$Path, [string]$Value) {
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Value, $encoding)
}

function Assert-NoUtf8Bom([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw "Packaged file must be UTF-8 without BOM: $Path"
    }
}

New-Item -ItemType Directory -Force -Path $outputRootPath | Out-Null

$safeVersion = $Version -replace '[^0-9A-Za-z_.-]', '-'
$stageRoot = Join-Path $repoRoot ".tmp\editor-vsix\code-language-vscode-$safeVersion"
$extensionStage = Join-Path $stageRoot "extension"
$syntaxStage = Join-Path $extensionStage "syntaxes"
$vsixPath = Join-Path $outputRootPath "code-language-vscode-$Version.vsix"
$temporaryZipPath = "$vsixPath.zip"

if (Test-Path $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
if (Test-Path $vsixPath) {
    Remove-Item -LiteralPath $vsixPath -Force
}
if (Test-Path $temporaryZipPath) {
    Remove-Item -LiteralPath $temporaryZipPath -Force
}

New-Item -ItemType Directory -Force -Path $syntaxStage | Out-Null

Copy-Item -LiteralPath (Join-Path $sourceExtensionPath "package.json") -Destination (Join-Path $extensionStage "package.json")
Copy-Item -LiteralPath (Join-Path $sourceExtensionPath "language-configuration.json") -Destination (Join-Path $extensionStage "language-configuration.json")
Copy-Item -LiteralPath $sourceGrammarPath -Destination (Join-Path $syntaxStage "code.tmLanguage.json")
Copy-Item -LiteralPath $sourceReadmePath -Destination (Join-Path $extensionStage "README.md")

$packagePath = Join-Path $extensionStage "package.json"
$packageJson = Get-Content -Path $packagePath -Raw
$packageJson = $packageJson.Replace('"version": "0.0.0"', "`"version`": `"$Version`"")
$packageJson = $packageJson.Replace('"path": "../textmate/code.tmLanguage.json"', '"path": "./syntaxes/code.tmLanguage.json"')
Write-Utf8NoBom $packagePath $packageJson

$contentTypes = @"
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="json" ContentType="application/json" />
  <Default Extension="md" ContentType="text/markdown" />
  <Default Extension="xml" ContentType="text/xml" />
</Types>
"@
Write-Utf8NoBom (Join-Path $stageRoot "[Content_Types].xml") $contentTypes

$escapedVersion = Escape-Xml $Version
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011">
  <Metadata>
    <Identity Language="en-US" Id="code-language" Version="$escapedVersion" Publisher="james-calvin" />
    <DisplayName>Code Language</DisplayName>
    <Description xml:space="preserve">Syntax highlighting for the Code programming language.</Description>
    <Tags>code,language,syntax</Tags>
    <Categories>Programming Languages</Categories>
    <Properties>
      <Property Id="Microsoft.VisualStudio.Code.Engine" Value="^1.85.0" />
    </Properties>
  </Metadata>
  <Installation>
    <InstallationTarget Id="Microsoft.VisualStudio.Code" />
  </Installation>
  <Dependencies />
  <Assets>
    <Asset Type="Microsoft.VisualStudio.Code.Manifest" Path="extension/package.json" Addressable="true" />
  </Assets>
</PackageManifest>
"@
Write-Utf8NoBom (Join-Path $stageRoot "extension.vsixmanifest") $manifest

Assert-NoUtf8Bom $packagePath
Assert-NoUtf8Bom (Join-Path $extensionStage "language-configuration.json")
Assert-NoUtf8Bom (Join-Path $syntaxStage "code.tmLanguage.json")

Compress-Archive -Path (Join-Path $stageRoot "*") -DestinationPath $temporaryZipPath -Force
Move-Item -LiteralPath $temporaryZipPath -Destination $vsixPath -Force

Write-Host "Packaged VS Code extension: $vsixPath"
