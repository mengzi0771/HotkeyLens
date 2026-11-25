[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.1.1',
    [string]$Runtime = 'win-x64',
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\HotkeyLens.App\HotkeyLens.App.csproj'
$publishDir = Join-Path $root 'artifacts\publish'
$portableDir = Join-Path $root 'artifacts\portable'
$distDir = Join-Path $root 'artifacts\dist'
$portableZip = Join-Path $distDir 'HotkeyLens-Portable-win-x64.zip'
$setupExe = Join-Path $distDir 'HotkeyLens-Setup.exe'
$checksumFile = Join-Path $distDir 'SHA256SUMS.txt'

function Resolve-InnoCompiler([string]$candidate) {
    if ($candidate) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Inno Setup compiler not found: $candidate"
        }
        return (Resolve-Path -LiteralPath $candidate).Path
    }

    $known = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) }

    if ($known) {
        return (Resolve-Path -LiteralPath $known[0]).Path
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    throw 'ISCC.exe was not found. Install Inno Setup 6 or pass -InnoCompiler <path>.'
}

function Invoke-Checked([string]$label, [scriptblock]$action) {
    & $action
    if ($LASTEXITCODE -ne 0) {
        throw "$label failed with exit code $LASTEXITCODE."
    }
}

$inno = Resolve-InnoCompiler $InnoCompiler
Write-Host "HotkeyLens release $Version ($Runtime)"
Write-Host "Inno Setup: $inno"

foreach ($directory in @($publishDir, $portableDir, $distDir)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

# Self-contained: the machine running HotkeyLens does not need a .NET runtime.
Invoke-Checked 'dotnet publish' {
    & dotnet publish $project `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        --output $publishDir `
        --nologo `
        -p:Version=$Version `
        -p:FileVersion="$Version.0" `
        -p:InformationalVersion=$Version `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:IncludeSourceRevisionInInformationalVersion=false
}

# The portable package is the same published payload plus a short usage note.
Copy-Item -Path (Join-Path $publishDir '*') -Destination $portableDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README-PORTABLE.txt') -Destination $portableDir -Force
# The marker is what InstallationModeDetector uses to distinguish a portable copy.
Set-Content -LiteralPath (Join-Path $portableDir 'portable.marker') -Value 'HotkeyLens portable build' -Encoding utf8NoBOM
Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $portableZip -CompressionLevel Optimal -Force

Invoke-Checked 'Inno Setup compile' {
    & $inno `
        "/DMyAppVersion=$Version" `
        "/DMyOutputBaseFilename=HotkeyLens-Setup" `
        '/Qp' `
        (Join-Path $root 'installer\HotkeyLens.iss')
}

if (-not (Test-Path -LiteralPath $setupExe -PathType Leaf)) {
    throw "Installer was not produced: $setupExe"
}
if (-not (Test-Path -LiteralPath $portableZip -PathType Leaf)) {
    throw "Portable archive was not produced: $portableZip"
}

$hashLines = @(
    (Get-FileHash -LiteralPath $setupExe -Algorithm SHA256),
    (Get-FileHash -LiteralPath $portableZip -Algorithm SHA256)
) | ForEach-Object {
    '{0}  {1}' -f $_.Hash.ToLowerInvariant(), (Split-Path -Leaf $_.Path)
}
Set-Content -LiteralPath $checksumFile -Value $hashLines -Encoding utf8NoBOM

Write-Host ''
Write-Host 'Release artifacts:'
Get-ChildItem -LiteralPath $distDir | ForEach-Object {
    Write-Host ('  {0}  {1:N0} bytes' -f $_.FullName, $_.Length)
}
Write-Host ''
Get-Content -LiteralPath $checksumFile