[CmdletBinding()]
param(
    [string]$BuildDirectory = 'build',
    [int64]$MaxAppBytes = 1048576,
    [int64]$MaxDiramBytes = 196608,
    [int64]$ExpectedDiramBytes = 341760,
    [int64]$MaxStackFrameBytes = 1024,
    [int64]$ExpectedRxPoolBytes = 3360,
    [int64]$MaxRadioRxCallbackFrameBytes = 64,
    [int64]$MaxNodeTaskFrameBytes = 96,
    [string]$WebBundlePath,
    [int64]$MaxWebBundleBytes = 32768
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot

function Resolve-ProjectPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw 'Path must not be empty.'
    }
    if (-not [IO.Path]::IsPathRooted($Path)) {
        $Path = Join-Path $projectDirectory $Path
    }
    return [IO.Path]::GetFullPath($Path)
}

foreach ($limit in @(
    @('MaxAppBytes', $MaxAppBytes),
    @('MaxDiramBytes', $MaxDiramBytes),
    @('ExpectedDiramBytes', $ExpectedDiramBytes),
    @('MaxStackFrameBytes', $MaxStackFrameBytes),
    @('ExpectedRxPoolBytes', $ExpectedRxPoolBytes),
    @('MaxRadioRxCallbackFrameBytes', $MaxRadioRxCallbackFrameBytes),
    @('MaxNodeTaskFrameBytes', $MaxNodeTaskFrameBytes),
    @('MaxWebBundleBytes', $MaxWebBundleBytes)
)) {
    if ([int64]$limit[1] -le 0) {
        throw "$($limit[0]) must be positive."
    }
}

if ((-not $env:IDF_PATH) -or (-not $env:IDF_PYTHON_ENV_PATH)) {
    throw 'Activate ESP-IDF v5.5.5 before running capacity verification.'
}

$BuildDirectory = Resolve-ProjectPath $BuildDirectory
$descriptionPath = Join-Path $BuildDirectory 'project_description.json'
if (-not (Test-Path -LiteralPath $descriptionPath -PathType Leaf)) {
    throw "Missing build description: $descriptionPath"
}

$description = Get-Content -LiteralPath $descriptionPath -Raw |
    ConvertFrom-Json
if (($description.project_version -ne '0.16.0') -or ($description.target -ne 'esp32s3') -or
    ($description.git_revision -ne 'v5.5.5')) {
    throw "Capacity contract requires esp32s3 and ESP-IDF v5.5.5; got target='$($description.target)' IDF='$($description.git_revision)'."
}

$appBin = Join-Path $BuildDirectory $description.app_bin
$mapName = [IO.Path]::ChangeExtension($description.app_elf, '.map')
$mapPath = Join-Path $BuildDirectory $mapName
foreach ($artifact in @($appBin, $mapPath)) {
    if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
        throw "Missing capacity artifact: $artifact"
    }
}

$mapText = Get-Content -LiteralPath $mapPath -Raw
$rxPoolPattern = '(?m)^ \.bss\.s_rx_pool\r?\n' +
    '\s+0x[0-9a-fA-F]+\s+0x([0-9a-fA-F]+)\s+'
$rxPoolMatch = [regex]::Match($mapText, $rxPoolPattern)
if (-not $rxPoolMatch.Success) {
    throw 'Linked map does not contain the fixed TriLink RX pool.'
}
$rxPoolBytes = [Convert]::ToInt64($rxPoolMatch.Groups[1].Value, 16)
if ($rxPoolBytes -ne $ExpectedRxPoolBytes) {
    throw "TriLink RX pool drifted: expected $ExpectedRxPoolBytes bytes, got $rxPoolBytes."
}
if ($mapText -match '(?m)\bradio_rx_task\b') {
    throw 'Dedicated radio RX task returned; v0.5 requires direct node ownership.'
}
if ($mapText -notmatch '(?m)\btrilink_frame_decode_view\b') {
    throw 'The zero-copy TriLink frame decoder is not linked.'
}

$serviceObjects = @(Get-ChildItem -LiteralPath (
    Join-Path $BuildDirectory 'esp-idf\trilink_protocol') -Recurse `
    -Filter 'trilink_service.c.obj' -File)
if ($serviceObjects.Count -ne 1) {
    throw "Expected one compiled service codec object, got $($serviceObjects.Count)."
}
$serviceCodecLinked = $mapText -match '(?m)\btrilink_service_(?:id_is_valid|encode|decode_view)\b'

$contentObjects = @(Get-ChildItem -LiteralPath (
    Join-Path $BuildDirectory 'esp-idf\trilink_protocol') -Recurse `
    -Filter 'trilink_content.c.obj' -File)
if ($contentObjects.Count -ne 1) {
    throw "Expected one compiled content codec object, got $($contentObjects.Count)."
}
$contentCodecLinked = $mapText -match '(?m)\btrilink_content_[a-z0-9_]+\b'

$fileWireObjects = @(Get-ChildItem -LiteralPath (
    Join-Path $BuildDirectory 'esp-idf\trilink_protocol') -Recurse `
    -Filter 'trilink_file_wire.c.obj' -File)
if ($fileWireObjects.Count -ne 1) {
    throw "Expected one compiled file wire-codec object, got $($fileWireObjects.Count)."
}
$fileWireCodecLinked = $mapText -match '(?m)\btrilink_file_[a-z0-9_]+\b'

if (-not $serviceCodecLinked -or $contentCodecLinked -or $fileWireCodecLinked) {
    throw 'v0.16.0 requires service/route/application dispatch, while content/file codecs remain dormant.'
}
foreach ($requiredSymbol in @('trilink_route_input', 'trilink_route_tick', 'trilink_route_lookup', 'trilink_route_neighbors', 'trilink_bridge_process', 'trilink_app_register', 'trilink_app_receive', 'trilink_app_tick', 'trilink_app_capabilities', 'trilink_room_input', 'trilink_room_command', 'trilink_rgb_begin', 'led_strip_new_rmt_device', 'tx_worker')) {
    if ($mapText -notmatch ('(?m)\b' + $requiredSymbol + '\b')) {
        throw "Missing active auto-route symbol: $requiredSymbol"
    }
}
foreach ($requiredSymbol in @('tinyusb_driver_install', 'tinyusb_net_init', 'trilink_web_start', 'trilink_web_take', 'trilink_usb_mux_begin')) {
    if ($mapText -notmatch ('(?m)\b' + $requiredSymbol + '\b')) {
        throw "Missing active CDC/NCM management symbol: $requiredSymbol"
    }
}

$routeMatch = [regex]::Match($mapText,
    '(?m)^ \.bss\.s_route\s+0x[0-9a-fA-F]+\s+0x([0-9a-fA-F]+)\s+')
if (-not $routeMatch.Success) { throw 'Linked route state is missing.' }
$routeStateBytes = [Convert]::ToInt64($routeMatch.Groups[1].Value, 16)
if ($routeStateBytes -gt 4096) { throw 'Route state exceeds the 4 KiB static budget.' }

$roomMatch = [regex]::Match($mapText,
    '(?m)^ \.bss\.s_room\s+0x[0-9a-fA-F]+\s+0x([0-9a-fA-F]+)\s+')
if (-not $roomMatch.Success) { throw 'Linked Room state is missing.' }
$roomStateBytes = [Convert]::ToInt64($roomMatch.Groups[1].Value, 16)
if ($roomStateBytes -gt 6144) { throw 'Room state exceeds the 6 KiB static budget.' }

$appDispatchMatch = [regex]::Match($mapText,
    '(?m)^ \.bss\.s_apps\s+0x[0-9a-fA-F]+\s+0x([0-9a-fA-F]+)\s+')
if (-not $appDispatchMatch.Success) { throw 'Linked node application registry is missing.' }
$appDispatchBytes = [Convert]::ToInt64($appDispatchMatch.Groups[1].Value, 16)
if ($appDispatchBytes -gt 128) { throw 'Application registry exceeds its 128 B static budget.' }

$python = Join-Path $env:IDF_PYTHON_ENV_PATH 'Scripts\python.exe'
$idfSize = Join-Path $env:IDF_PATH 'tools\idf_size.py'
foreach ($tool in @($python, $idfSize)) {
    if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) {
        throw "Missing ESP-IDF capacity tool: $tool"
    }
}

$idfVersionDirectory = Split-Path -Leaf (
    Split-Path -Parent ([IO.Path]::GetFullPath($env:IDF_PATH)))
if ($idfVersionDirectory -ne 'v5.5.5') {
    throw "Expected active ESP-IDF v5.5.5 path, got '$($env:IDF_PATH)'."
}

$savedErrorActionPreference = $ErrorActionPreference
try {
    # Windows PowerShell 5.1 can promote native stderr to ErrorRecord objects.
    # Capture all output and decide from the native exit code.
    $ErrorActionPreference = 'Continue'
    $sizeOutput = (& $python $idfSize --format json $mapPath 2>&1 |
        Out-String)
    $sizeExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $savedErrorActionPreference
}
if ($sizeExitCode -ne 0) {
    throw "idf_size failed with exit code $sizeExitCode."
}

try {
    $size = $sizeOutput | ConvertFrom-Json
}
catch {
    throw "idf_size did not return valid JSON: $($_.Exception.Message)"
}

$appBytes = (Get-Item -LiteralPath $appBin).Length
$diramBytes = [int64]$size.used_diram
$diramTotal = [int64]$size.diram_total
if ($diramTotal -ne $ExpectedDiramBytes) {
    throw "Unexpected DIRAM total: expected $ExpectedDiramBytes, got $diramTotal."
}
if ($appBytes -gt $MaxAppBytes) {
    throw "Application capacity exceeded: $appBytes > $MaxAppBytes bytes."
}
if ($diramBytes -gt $MaxDiramBytes) {
    throw "Static DIRAM capacity exceeded: $diramBytes > $MaxDiramBytes bytes."
}

$stackUsageFiles = @(Get-ChildItem -LiteralPath $BuildDirectory -Recurse `
    -Filter '*.su' -File | Where-Object {
        $_.FullName -match '[\\/](?:main|trilink_[^\\/]+|wakenic_[^\\/]+)[\\/].*\.su$'
    })
if ($stackUsageFiles.Count -eq 0) {
    throw 'No project stack-usage files were found; compile options may have drifted.'
}
$stackRows = @()
foreach ($stackUsageFile in $stackUsageFiles) {
    foreach ($line in Get-Content -LiteralPath $stackUsageFile.FullName) {
        $fields = @($line -split "`t")
        [int64]$bytes = 0
        if (($fields.Count -ge 3) -and
            [int64]::TryParse($fields[1], [ref]$bytes)) {
            $functionName = $fields[0] -replace '^.*:\d+:\d+:', ''
            $stackRows += [pscustomobject]@{
                Function = $functionName
                Bytes = $bytes
                Kind = $fields[2]
                File = $stackUsageFile.Name
            }
        }
    }
}
if ($stackRows.Count -eq 0) {
    throw 'Project stack-usage files contained no parseable records.'
}
$largestStackFrame = $stackRows | Sort-Object Bytes -Descending |
    Select-Object -First 1
if ($largestStackFrame.Bytes -gt $MaxStackFrameBytes) {
    throw "Static stack frame exceeded: $($largestStackFrame.Function) uses $($largestStackFrame.Bytes) > $MaxStackFrameBytes bytes."
}

function Assert-FunctionStackBound(
    [string]$Function,
    [int64]$Maximum
) {
    $matches = @($stackRows | Where-Object { $_.Function -eq $Function })
    if ($matches.Count -ne 1) {
        throw "Expected one stack-usage record for $Function, got $($matches.Count)."
    }
    if ($matches[0].Bytes -gt $Maximum) {
        throw "$Function static frame exceeded: $($matches[0].Bytes) > $Maximum bytes."
    }
    return [int64]$matches[0].Bytes
}

$radioRxCallbackFrameBytes = Assert-FunctionStackBound `
    'radio_receive_cb' $MaxRadioRxCallbackFrameBytes
$nodeTaskFrameBytes = Assert-FunctionStackBound `
    'trilink_node_task' $MaxNodeTaskFrameBytes

$webBytes = $null
if ([string]::IsNullOrWhiteSpace($WebBundlePath)) {
    $webBytes = 0L
    foreach ($asset in @('index.html','app.js','app.css')) {
        $assetPath = Join-Path $projectDirectory ('components\trilink_web\www\' + $asset)
        if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) { throw "Missing embedded web asset: $asset" }
        $webBytes += (Get-Item -LiteralPath $assetPath).Length
    }
    if ($webBytes -gt $MaxWebBundleBytes) { throw "Embedded web assets exceed budget: $webBytes > $MaxWebBundleBytes" }
}
if (-not [string]::IsNullOrWhiteSpace($WebBundlePath)) {
    $WebBundlePath = Resolve-ProjectPath $WebBundlePath
    if (-not (Test-Path -LiteralPath $WebBundlePath -PathType Leaf)) {
        throw "Web bundle is missing: $WebBundlePath"
    }
    $webBytes = (Get-Item -LiteralPath $WebBundlePath).Length
    if ($webBytes -gt $MaxWebBundleBytes) {
        throw "Web bundle capacity exceeded: $webBytes > $MaxWebBundleBytes bytes."
    }
}

[pscustomobject]@{
    Result = 'PASS'
    Project = $description.project_name
    Target = $description.target
    AppBytes = $appBytes
    MaxAppBytes = $MaxAppBytes
    AppHeadroomBytes = $MaxAppBytes - $appBytes
    DiramBytes = $diramBytes
    MaxDiramBytes = $MaxDiramBytes
    DiramHeadroomBytes = $MaxDiramBytes - $diramBytes
    DiramPhysicalRemainBytes = $diramTotal - $diramBytes
    MaxStackFrameBytes = $MaxStackFrameBytes
    LargestStackFrameBytes = $largestStackFrame.Bytes
    LargestStackFrameFunction = $largestStackFrame.Function
    RxPoolBytes = $rxPoolBytes
    RouteStateBytes = $routeStateBytes
    RoomStateBytes = $roomStateBytes
    AppDispatcherBytes = $appDispatchBytes
    AppDispatcherLinked = $true
    DedicatedRadioRxTask = $false
    RadioRxCallbackFrameBytes = $radioRxCallbackFrameBytes
    NodeTaskFrameBytes = $nodeTaskFrameBytes
    ZeroCopyFrameViewLinked = $true
    ServiceCodecObjectBuilt = $true
    ServiceCodecLinkedInCurrentApp = $serviceCodecLinked
    ContentCodecObjectBuilt = $true
    ContentCodecLinkedInCurrentApp = $contentCodecLinked
    FileWireCodecObjectBuilt = $true
    FileWireCodecLinkedInCurrentApp = $fileWireCodecLinked
    WebBundleBytes = $webBytes
    MaxWebBundleBytes = $MaxWebBundleBytes
    UsbCdcNcmLinked = $true
    RuntimeHeapAndStackHighWaterVerified = $false
    StaticOnly = $true
}
