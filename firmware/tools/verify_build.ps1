[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$BuildDirectory,

    [string]$SdkconfigPath
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot

function Resolve-ProjectPath([string]$Path) {
    if (-not [IO.Path]::IsPathRooted($Path)) {
        $Path = Join-Path $projectDirectory $Path
    }
    return [IO.Path]::GetFullPath($Path)
}

function Normalize-Path([string]$Path) {
    return (Resolve-ProjectPath $Path).TrimEnd('\', '/')
}

function Assert-SamePath(
    [string]$Actual,
    [string]$Expected,
    [string]$Label
) {
    $normalizedActual = Normalize-Path $Actual
    $normalizedExpected = Normalize-Path $Expected
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals(
            $normalizedActual,
            $normalizedExpected)) {
        throw "$Label mismatch. Expected '$normalizedExpected', got '$normalizedActual'."
    }
}

function Convert-ToByteCount([string]$Value) {
    $text = $Value.Trim()
    if ($text -match '^0x[0-9a-fA-F]+$') {
        return [Convert]::ToInt64($text.Substring(2), 16)
    }
    if ($text -notmatch '^(\d+)([KMG])?$') {
        throw "Unsupported partition size or offset: $Value"
    }
    $number = [int64]$Matches[1]
    switch ($Matches[2]) {
        'K' { return $number * 1KB }
        'M' { return $number * 1MB }
        'G' { return $number * 1GB }
        default { return $number }
    }
}

$BuildDirectory = Resolve-ProjectPath $BuildDirectory
$descriptionPath = Join-Path $BuildDirectory 'project_description.json'
$flasherArgsPath = Join-Path $BuildDirectory 'flasher_args.json'
$buildNinjaPath = Join-Path $BuildDirectory 'build.ninja'
$bootloaderDescriptionPath = Join-Path $BuildDirectory (
    'bootloader\project_description.json')
$bootloaderNinjaPath = Join-Path $BuildDirectory 'bootloader\build.ninja'

foreach ($requiredPath in @(
    $descriptionPath,
    $flasherArgsPath,
    $buildNinjaPath,
    $bootloaderDescriptionPath,
    $bootloaderNinjaPath
)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required build artifact is missing: $requiredPath"
    }
}

$description = Get-Content -LiteralPath $descriptionPath -Raw |
    ConvertFrom-Json
if (($description.project_name -ne 'esp32s3_wakenic_p2p') -or
    ($description.project_version -ne '0.16.0') -or
    ($description.target -ne 'esp32s3') -or
    ($description.git_revision -ne 'v5.5.5') -or
    ($description.build_type -ne 'flash_app')) {
    throw 'Project name, version, target, ESP-IDF revision or build type does not match the TriLink skeleton contract.'
}

Assert-SamePath $description.project_path $projectDirectory 'Project path'
Assert-SamePath $description.build_dir $BuildDirectory 'Build directory'
Assert-SamePath $description.config_defaults (
    Join-Path $projectDirectory 'sdkconfig.defaults') 'Config defaults'

if ([string]::IsNullOrWhiteSpace($SdkconfigPath)) {
    $SdkconfigPath = Normalize-Path $description.config_file
} else {
    $SdkconfigPath = Resolve-ProjectPath $SdkconfigPath
}
Assert-SamePath $description.config_file $SdkconfigPath 'Generated sdkconfig'

if (($description.app_bin -ne 'esp32s3_wakenic_p2p.bin') -or
    ($description.app_elf -ne 'esp32s3_wakenic_p2p.elf')) {
    throw 'Project description points to an unexpected application artifact.'
}

$appBin = Join-Path $BuildDirectory $description.app_bin
$appElf = Join-Path $BuildDirectory $description.app_elf
$bootloaderBin = Join-Path $BuildDirectory 'bootloader\bootloader.bin'
$bootloaderElf = Join-Path $BuildDirectory 'bootloader\bootloader.elf'
$partitionBin = Join-Path $BuildDirectory 'partition_table\partition-table.bin'
Assert-SamePath $description.bootloader_elf $bootloaderElf 'Bootloader ELF'

foreach ($requiredPath in @(
    $SdkconfigPath,
    $appBin,
    $appElf,
    $bootloaderBin,
    $bootloaderElf,
    $partitionBin
)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required build artifact is missing: $requiredPath"
    }
}
foreach ($nonemptyArtifact in @(
    $appBin,
    $appElf,
    $bootloaderBin,
    $bootloaderElf,
    $partitionBin
)) {
    if ((Get-Item -LiteralPath $nonemptyArtifact).Length -le 0) {
        throw "Build artifact is empty: $nonemptyArtifact"
    }
}

$configLines = Get-Content -LiteralPath $SdkconfigPath
$requiredConfig = @(
    'CONFIG_IDF_TARGET="esp32s3"',
    'CONFIG_ESPTOOLPY_FLASHSIZE_16MB=y',
    'CONFIG_ESPTOOLPY_FLASHMODE_DIO=y',
    'CONFIG_ESPTOOLPY_FLASHFREQ_80M=y',
    'CONFIG_SPIRAM=y',
    'CONFIG_SPIRAM_MODE_OCT=y',
    'CONFIG_SPIRAM_SPEED_80M=y',
    'CONFIG_ESP_CONSOLE_UART_DEFAULT=y',
    'CONFIG_ESP_CONSOLE_SECONDARY_NONE=y',
    '# CONFIG_ESP_CONSOLE_USB_SERIAL_JTAG is not set',
    '# CONFIG_ESP_CONSOLE_SECONDARY_USB_SERIAL_JTAG is not set'
    'CONFIG_TINYUSB_CDC_ENABLED=y',
    'CONFIG_TINYUSB_NET_MODE_NCM=y'
)
foreach ($expected in $requiredConfig) {
    if ($configLines -notcontains $expected) {
        throw "Generated configuration is missing: $expected"
    }
}

$minimalBuild = Select-String -LiteralPath $buildNinjaPath `
    -SimpleMatch 'IDF_MINIMAL_BUILD=y' -Quiet
if (-not $minimalBuild) {
    throw 'Top-level build.ninja does not prove IDF_MINIMAL_BUILD=y.'
}

$components = @($description.build_components |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
foreach ($requiredComponent in @(
    'main',
    'trilink_cluster',
    'trilink_node',
    'trilink_protocol',
    'trilink_radio',
    'esp_netif',
    'esp_wifi',
    'nvs_flash',
    'wakenic_board',
    'wakenic_core',
    'trilink_usb',
    'trilink_web',
    'esp_http_server',
    'espressif__esp_tinyusb',
    'espressif__tinyusb'
)) {
    if ($components -notcontains $requiredComponent) {
        throw "Required project component is missing: $requiredComponent"
    }
}
# USB OTG now belongs to TinyUSB; UART0 retains the recovery/log path.

$bootloaderDescription = Get-Content -LiteralPath $bootloaderDescriptionPath -Raw |
    ConvertFrom-Json
$bootloaderComponents = @($bootloaderDescription.build_components |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($bootloaderComponents -notcontains 'xmc_7018_compat') {
    throw 'Bootloader does not include the verified XMC 0x207018 compatibility component.'
}
$xmcWrap = Select-String -LiteralPath $bootloaderNinjaPath `
    -SimpleMatch '--wrap=bootloader_flash_xmc_startup' -Quiet
if (-not $xmcWrap) {
    throw 'Bootloader link does not prove the narrow XMC startup wrapper.'
}

if ((-not $env:IDF_PATH) -or (-not $env:IDF_PYTHON_ENV_PATH)) {
    throw 'Activate ESP-IDF v5.5.5 before running build verification.'
}
$python = Join-Path $env:IDF_PYTHON_ENV_PATH 'Scripts\python.exe'
$partitionTool = Join-Path $env:IDF_PATH (
    'components\partition_table\gen_esp32part.py')
foreach ($requiredTool in @($python, $partitionTool)) {
    if (-not (Test-Path -LiteralPath $requiredTool -PathType Leaf)) {
        throw "Required ESP-IDF verification tool is missing: $requiredTool"
    }
}

$flasher = Get-Content -LiteralPath $flasherArgsPath -Raw |
    ConvertFrom-Json
if (($flasher.flash_settings.flash_mode -ne 'dio') -or
    ($flasher.flash_settings.flash_size -ne '16MB') -or
    ($flasher.flash_settings.flash_freq -ne '80m') -or
    ($flasher.extra_esptool_args.chip -ne 'esp32s3')) {
    throw 'Flasher settings do not match the board baseline.'
}

$flashEntries = @(
    @($flasher.bootloader, $bootloaderBin, 'Bootloader'),
    @($flasher.'partition-table', $partitionBin, 'Partition table'),
    @($flasher.app, $appBin, 'Application')
)
foreach ($flashEntry in $flashEntries) {
    $entry = $flashEntry[0]
    $expectedPath = $flashEntry[1]
    $label = $flashEntry[2]
    Assert-SamePath (Join-Path $BuildDirectory $entry.file) $expectedPath "$label flash file"
}

$flashOffsets = @(
    $flasher.bootloader.offset,
    $flasher.'partition-table'.offset,
    $flasher.app.offset
)
if (@($flashOffsets | Sort-Object -Unique).Count -ne $flashOffsets.Count) {
    throw 'Flasher entry offsets must be distinct.'
}
$flashFileProperties = @($flasher.flash_files.PSObject.Properties)
if ($flashFileProperties.Count -ne $flashEntries.Count) {
    throw "flash_files must contain exactly $($flashEntries.Count) entries."
}
for ($index = 0; $index -lt $flashEntries.Count; ++$index) {
    $entry = $flashEntries[$index][0]
    $expectedPath = $flashEntries[$index][1]
    $matchingProperties = @($flashFileProperties |
        Where-Object { $_.Name -eq $entry.offset })
    if ($matchingProperties.Count -ne 1) {
        throw "flash_files must contain exactly one entry at $($entry.offset)."
    }
    Assert-SamePath (
        Join-Path $BuildDirectory $matchingProperties[0].Value) `
        $expectedPath "flash_files[$($entry.offset)]"
}

$bootloaderOffset = Convert-ToByteCount $flasher.bootloader.offset
$partitionOffset = Convert-ToByteCount $flasher.'partition-table'.offset
$appOffset = Convert-ToByteCount $flasher.app.offset
$appSize = (Get-Item -LiteralPath $appBin).Length
$bootloaderSize = (Get-Item -LiteralPath $bootloaderBin).Length
$partitionBinarySize = (Get-Item -LiteralPath $partitionBin).Length

if ($partitionOffset -le $bootloaderOffset) {
    throw 'Partition-table offset must follow the bootloader offset.'
}
$bootloaderCapacity = $partitionOffset - $bootloaderOffset
if ($bootloaderSize -gt $bootloaderCapacity) {
    throw "Bootloader exceeds its generated flash region: $bootloaderSize bytes."
}

$savedErrorActionPreference = $ErrorActionPreference
try {
    # Windows PowerShell 5.1 promotes native stderr to ErrorRecord objects when
    # Stop is active, even if the native process succeeds. Capture under
    # Continue and decide exclusively from the native exit code.
    $ErrorActionPreference = 'Continue'
    $partitionOutput = @(& $python $partitionTool $partitionBin 2>&1)
    $partitionExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $savedErrorActionPreference
}
if ($partitionExitCode -ne 0) {
    throw "Partition-table decoding failed with exit code $partitionExitCode"
}
$partitionCsv = @($partitionOutput |
    ForEach-Object { $_.ToString().Trim() } |
    Where-Object { ($_ -notmatch '^(?:#|Parsing |Verifying )') -and ($_ -match ',') })
if ($partitionCsv.Count -eq 0) {
    throw 'Partition-table decoder returned no CSV records.'
}
$partitionRows = @($partitionCsv |
    ConvertFrom-Csv -Header Name, Type, SubType, Offset, Size, Flags)
$matchingAppPartitions = @($partitionRows | Where-Object {
    ($_.Type -eq 'app') -and
    ((Convert-ToByteCount $_.Offset) -eq $appOffset)
})
if ($matchingAppPartitions.Count -ne 1) {
    throw "Expected exactly one app partition at generated offset $($flasher.app.offset)."
}
$appPartitionSize = Convert-ToByteCount $matchingAppPartitions[0].Size
if ($appSize -gt $appPartitionSize) {
    throw "Application image exceeds its generated partition: $appSize bytes."
}

$firstPartitionOffset = ($partitionRows |
    ForEach-Object { Convert-ToByteCount $_.Offset } |
    Measure-Object -Minimum).Minimum
if (($partitionOffset + $partitionBinarySize) -gt $firstPartitionOffset) {
    throw 'Partition-table binary overlaps the first generated partition.'
}

try {
    $ErrorActionPreference = 'Continue'
    $bootloaderImageInfo = (& $python -m esptool image_info --version 2 `
        $bootloaderBin 2>&1 | Out-String)
    $bootloaderImageInfoExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $savedErrorActionPreference
}
if ($bootloaderImageInfoExitCode -ne 0) {
    throw "Bootloader image_info failed with exit code $bootloaderImageInfoExitCode"
}
foreach ($pattern in @(
    'Detected image type: ESP32-S3',
    'Flash size: 16MB',
    'Flash freq: 80m',
    'Flash mode: DIO',
    'ESP-IDF: v5.5.5',
    'Checksum: .*\(valid\)',
    'Validation hash: .*\(valid\)'
)) {
    if ($bootloaderImageInfo -notmatch $pattern) {
        throw "Bootloader metadata check failed: $pattern"
    }
}

try {
    $ErrorActionPreference = 'Continue'
    $imageInfo = (& $python -m esptool image_info --version 2 $appBin 2>&1 |
        Out-String)
    $imageInfoExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $savedErrorActionPreference
}
if ($imageInfoExitCode -ne 0) {
    throw "esptool image_info failed with exit code $imageInfoExitCode"
}
foreach ($pattern in @(
    'Detected image type: ESP32-S3',
    'Flash size: 16MB',
    'Flash freq: 80m',
    'Flash mode: DIO',
    'Project name: esp32s3_wakenic_p2p',
    'App version: 0.16.0',
    'ESP-IDF: v5.5.5',
    'Checksum: .*\(valid\)',
    'Validation hash: .*\(valid\)'
)) {
    if ($imageInfo -notmatch $pattern) {
        throw "Image metadata check failed: $pattern"
    }
}

$hash = (Get-FileHash -LiteralPath $appBin -Algorithm SHA256).Hash
$bootloaderHash = (
    Get-FileHash -LiteralPath $bootloaderBin -Algorithm SHA256).Hash
[pscustomobject]@{
    Result = 'PASS'
    Project = $description.project_name
    Version = $description.project_version
    Target = $description.target
    MinimalBuild = $true
    Components = $components.Count
    AppOffset = ('0x{0:x}' -f $appOffset)
    AppBytes = $appSize
    AppPartitionBytes = $appPartitionSize
    BootloaderOffset = ('0x{0:x}' -f $bootloaderOffset)
    BootloaderBytes = $bootloaderSize
    BootloaderSha256 = $bootloaderHash
    PartitionTableOffset = ('0x{0:x}' -f $partitionOffset)
    AppSha256 = $hash
}
