[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$AbstractionsPath,
    [string]$CscPath,
    [string]$FrameworkPath
)

$ErrorActionPreference = 'Stop'
$taskProject = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskCompiler = if ([string]::IsNullOrWhiteSpace($CscPath)) {
    'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
} else { $CscPath }
$taskFramework = if ([string]::IsNullOrWhiteSpace($FrameworkPath)) {
    'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
} else { $FrameworkPath }
if (-not (Test-Path -LiteralPath $taskCompiler -PathType Leaf)) { throw 'Verified Roslyn compiler is unavailable.' }
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) ('trilink-serial-tests-' + [Guid]::NewGuid().ToString('N'))
}
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$taskOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
$taskOptions = @('/nologo', '/noconfig', '/nostdlib+', '/langversion:latest', '/warnaserror+', '/deterministic+', '/utf8output')
$taskReferences = @('mscorlib.dll', 'System.dll', 'System.Core.dll') | ForEach-Object {
    '/reference:' + (Join-Path $taskFramework $_)
}
if (-not $AbstractionsPath) {
    $AbstractionsPath = Join-Path $taskOutput 'TriLink.Plugin.Abstractions.dll'
    $taskAbstractionSources = @(Get-ChildItem -LiteralPath (Join-Path $taskProject 'src\TriLink.Plugin.Abstractions') -Filter '*.cs' -File | ForEach-Object FullName)
    & $taskCompiler @taskOptions @taskReferences ('/reference:' + (Join-Path $taskFramework 'System.Windows.Forms.dll')) `
        '/target:library' ('/out:' + $AbstractionsPath) @taskAbstractionSources
    if ($LASTEXITCODE -ne 0) { throw 'Serial test abstraction compilation failed.' }
}
$AbstractionsPath = (Resolve-Path -LiteralPath $AbstractionsPath).Path
$taskLocalAbstractions = Join-Path $taskOutput 'TriLink.Plugin.Abstractions.dll'
if ($AbstractionsPath -ne $taskLocalAbstractions) {
    Copy-Item -LiteralPath $AbstractionsPath -Destination $taskLocalAbstractions -Force
}
$taskSources = @(Get-ChildItem -LiteralPath (Join-Path $taskProject 'src\Plugins\TriLink.Plugin.Serial') -Filter '*.cs' -File | ForEach-Object FullName)
$taskExecutable = Join-Path $taskOutput 'TriLink.SerialLifecycle.Tests.exe'
& $taskCompiler @taskOptions @taskReferences ('/reference:' + $AbstractionsPath) '/target:exe' `
    ('/out:' + $taskExecutable) @taskSources (Join-Path $PSScriptRoot 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Serial lifecycle compilation failed.' }
& $taskExecutable
if ($LASTEXITCODE -ne 0) { throw 'Serial lifecycle regression failed.' }
Write-Host "PASS serial-lifecycle=$taskExecutable"
