[CmdletBinding()]
param(
    [string]$Compiler = 'D:\x86_64-8.1.0-release-posix-seh-rt_v6-rev0\mingw64\bin\gcc.exe',
    [string]$ClientDirectory,
    [string]$CscPath
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$parserRelative = 'src\Plugins\TriLink.Plugin.Serial\UsbControlProtocol.cs'
if ([string]::IsNullOrWhiteSpace($ClientDirectory)) {
    $parent = Split-Path -Parent $project
    foreach ($candidate in @($parent,
                             (Join-Path $parent 'trilink_game_branch'),
                             (Join-Path $parent 'trilink_min_client'))) {
        if (Test-Path -LiteralPath (Join-Path $candidate $parserRelative) -PathType Leaf) {
            $ClientDirectory = $candidate
            break
        }
    }
}
if ([string]::IsNullOrWhiteSpace($ClientDirectory)) {
    throw 'Client parser not found. Pass -ClientDirectory <client repository root>.'
}
$parser = Join-Path $ClientDirectory $parserRelative
if ([string]::IsNullOrWhiteSpace($CscPath)) {
    $installations = @()
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
            $installations += @(& $vswhere -products '*' -requires Microsoft.Component.MSBuild -property installationPath)
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($env:VSINSTALLDIR)) {
        $installations += $env:VSINSTALLDIR
    }
    foreach ($installation in $installations) {
        if ([string]::IsNullOrWhiteSpace($installation)) { continue }
        $candidate = Join-Path $installation 'MSBuild\Current\Bin\Roslyn\csc.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $CscPath = $candidate
            break
        }
    }
}
if ([string]::IsNullOrWhiteSpace($CscPath)) {
    throw 'Roslyn compiler not found. Install Visual Studio / Build Tools with MSBuild, or pass -CscPath <csc.exe>.'
}
$csc = $CscPath
foreach($file in @($Compiler, $csc, $parser)) {
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing test input: $file"}
}
$csc = (Resolve-Path -LiteralPath $csc).Path
$parser = (Resolve-Path -LiteralPath $parser).Path
Write-Host "USB contract parser: $parser"
Write-Host "USB contract C# compiler: $csc"
$out = Join-Path $project ('artifacts\tests\contract-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$fixture = Join-Path $out 'firmware_fixture.exe'
$verifier = Join-Path $out 'client_contract.exe'
& $Compiler -std=c11 -Wall -Wextra -Werror -pedantic "-I$project\components\trilink_protocol\include" `
    "$project\components\trilink_protocol\trilink_control.c" "$project\tests\host\test_trilink_control.c" -o $fixture
if($LASTEXITCODE -ne 0){throw 'Firmware fixture compilation failed'}
& $csc /nologo /target:exe "/out:$verifier" $parser "$project\tests\host\UsbContractCheck.cs"
if($LASTEXITCODE -ne 0){throw 'Client parser compilation failed'}
$lines = @(& $fixture --fixture)
if($LASTEXITCODE -ne 0){throw 'Firmware fixture generation failed'}
$lines | & $verifier
if($LASTEXITCODE -ne 0){throw 'Firmware/client wire compatibility failed'}
