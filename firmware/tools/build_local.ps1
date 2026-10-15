[CmdletBinding()]
param(
    [string]$IdfProfile = 'C:\Espressif\tools\Microsoft.v5.5.5.PowerShell_profile.ps1',
    [string]$ReuseStage,
    [string]$HostCompiler = 'D:\x86_64-8.1.0-release-posix-seh-rt_v6-rev0\mingw64\bin\gcc.exe',
    [string]$HostPython = 'C:\Espressif\tools\python\v5.5.5\venv\Scripts\python.exe',
    [string]$HostNode = 'node.exe',
    [string]$ClientDirectory,
    [string]$CscPath
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$buildId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$out = Join-Path $project ('artifacts\builds\' + $buildId)
New-Item -ItemType Directory -Path $out | Out-Null
& "$PSScriptRoot\test_host.ps1" -Compiler $HostCompiler -EvidenceDirectory $out *>&1 |
    Tee-Object -FilePath (Join-Path $out 'host-tests.log')
& "$PSScriptRoot\test_usb_contract.ps1" -Compiler $HostCompiler -ClientDirectory $ClientDirectory -CscPath $CscPath *>&1 |
    Tee-Object -FilePath (Join-Path $out 'usb-legacy-contract.log')
if(-not (Test-Path -LiteralPath $HostPython -PathType Leaf)){throw 'Host Python is missing.'}
& $HostPython -B "$project\tests\host\test_chain_probe.py"
if($LASTEXITCODE -ne 0){throw 'USB chain probe unit tests failed.'}
& $HostPython -B "$project\tests\host\test_mesh_probe.py"
if($LASTEXITCODE -ne 0){throw 'USB mesh probe unit tests failed.'}
& $HostNode "$project\tests\host\test_web_ui.js" *>&1 |
    Tee-Object -FilePath (Join-Path $out 'web-ui-tests.log')
if($LASTEXITCODE -ne 0){throw 'Embedded web UI state-machine tests failed.'}
$stage = Join-Path $env:TEMP ('trilink-fw-' + $buildId)
if(-not [string]::IsNullOrWhiteSpace($ReuseStage)) {
    $tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    $stage = (Resolve-Path -LiteralPath $ReuseStage).Path
    if(-not $stage.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $stage) -notmatch '^trilink-fw-\d{8}-\d{6}-[0-9a-f]{8}$') {
        throw 'ReuseStage must be an exact build_local stage under TEMP.'
    }
}
if($stage -match '[^\x00-\x7F]'){throw 'ESP-IDF requires an ASCII TEMP path for this Windows toolchain.'}
if(-not $ReuseStage -and (Test-Path -LiteralPath $stage)){throw "Refusing an existing staging directory: $stage"}
$source = Join-Path $stage 'source'
$build = Join-Path $stage 'build'
$config = Join-Path $stage 'sdkconfig'
New-Item -ItemType Directory -Path $source,$out -Force | Out-Null
$inputs = @('bootloader_components','components','main','tools','tests','CMakeLists.txt','sdkconfig.defaults')
$inputs += 'dependencies.lock'
foreach($name in $inputs){Copy-Item -LiteralPath (Join-Path $project $name) -Destination $source -Recurse -Force}

# Verify the mirror before invoking IDF; no junctions or writes to old projects.
$manifest = @(Get-ChildItem -LiteralPath $source -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/]__pycache__[\\/]' } | ForEach-Object {
    $relative = $_.FullName.Substring($source.Length + 1)
    $original = Join-Path $project $relative
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    if($hash -ne (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash){throw "Mirror mismatch: $relative"}
    [pscustomobject]@{Path=$relative; SHA256=$hash}
})
$manifest | Export-Csv -LiteralPath (Join-Path $out 'source-manifest.csv') -NoTypeInformation -Encoding UTF8
$fresh = -not [bool]$ReuseStage
& "$source\tools\build.ps1" -IdfProfile $IdfProfile -BuildDirectory $build -SdkconfigPath $config -NoCcache -RequireFresh:$fresh *>&1 |
    Tee-Object -FilePath (Join-Path $out 'build.log')
$verified = & "$source\tools\verify_build.ps1" -BuildDirectory $build -SdkconfigPath $config
$capacity = & "$source\tools\verify_capacity.ps1" -BuildDirectory $build
foreach ($input in $manifest) {
    if ($input.SHA256 -ne (Get-FileHash -LiteralPath (Join-Path $source $input.Path) -Algorithm SHA256).Hash -or
        $input.SHA256 -ne (Get-FileHash -LiteralPath (Join-Path $project $input.Path) -Algorithm SHA256).Hash) {
        throw "Source input changed during build; retain artifacts but do not publish success: $($input.Path)"
    }
}

foreach($name in @('esp32s3_wakenic_p2p.bin','bootloader\bootloader.bin','partition_table\partition-table.bin')){
    Copy-Item -LiteralPath (Join-Path $build $name) -Destination $out
}
$receipt = [pscustomobject]@{
    Version='0.16.0'; Stage=$stage; BuildDirectory=$build; Sdkconfig=$config;
    Artifacts=$out; BuildVerification=$verified; Capacity=$capacity;
    FreshBuild=$fresh; HostTestsPassed=$true; UsbLegacyContractPassed=$true; ProbeUnitTestsPassed=$true;
    SoftwareRoomRgbIntegrationPassed=$true;
    TinyUsbCdcNcmBuilt=$true; UsbLocalHttpBuilt=$true; Windows10CdcHardwareVerified=$false; NcmHardwareVerified=$false;
    WebUiFakeTestsPassed=$true;
    DependencyLockSha256=(Get-FileHash -LiteralPath (Join-Path $source 'dependencies.lock') -Algorithm SHA256).Hash;
    EspTinyUsbVersion='2.0.0'; TinyUsbVersion='0.18.0~2';
    NodeModuleDispatchPassed=$true; NodeDescriptorContractVersion=1; NodeDescriptorServiceMask='01';
    HardwareVerified=$false; RoomIntegrationVerified=$false; Flashed=$false; Uploaded=$false
}
$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $out 'verification.json') -Encoding UTF8
Write-Host "PASS firmware package: $out"
$receipt
