[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Stage,
    [string]$ProjectRoot,
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string]$Version = '0.16.0',
    [string]$FirmwareReceipt
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }
# This script packages only a previously completed isolated build. It never starts
# a client, accesses a device, stops a process, or removes an existing delivery.
# Failed partial deliveries are retained without a success receipt for inspection.

function Assert-NoReparsePath([string]$Path) {
    $candidate = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrEmpty($candidate)) {
        if (Test-Path -LiteralPath $candidate) {
            $item = Get-Item -LiteralPath $candidate -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse paths are not accepted: $candidate"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($candidate)
        if ($parent -eq $candidate) { break }
        $candidate = $parent
    }
}

function Resolve-Directory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { throw "Directory missing: $Path" }
    $resolved = (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\', '/')
    Assert-NoReparsePath $resolved
    return $resolved
}

function Normalize-InputPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path) -or $Path.Contains(':')) {
        throw "Invalid build input path: $Path"
    }
    $relative = $Path.Replace('/', '\')
    $parts = $relative.Split('\')
    if (@($parts | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' }).Count -ne 0) {
        throw "Unsafe build input path: $Path"
    }
    if ($relative -ne 'VERSION' -and ($parts.Length -lt 2 -or $parts[0] -notin @('src', 'tests', 'tools'))) {
        throw "Build input is outside src/tests/tools/VERSION: $Path"
    }
    return $relative
}

function Get-InputFiles([string]$Root) {
    foreach ($part in @('src', 'tests', 'tools')) {
        $directory = Join-Path $Root $part
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Input directory missing: $directory" }
        foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File -Force | Sort-Object FullName) {
            Assert-NoReparsePath $file.FullName
            [pscustomobject]@{ Path = $file.FullName.Substring($Root.Length + 1); FullName = $file.FullName }
        }
    }
    $versionPath = Join-Path $Root 'VERSION'
    if (-not (Test-Path -LiteralPath $versionPath -PathType Leaf)) { throw "VERSION missing: $versionPath" }
    Assert-NoReparsePath $versionPath
    [pscustomobject]@{ Path = 'VERSION'; FullName = $versionPath }
}

function Assert-FrozenInputs([string]$Project, [string]$BuildStage, $Recorded) {
    foreach ($root in @($Project, $BuildStage)) {
        $files = @(Get-InputFiles $root)
        if ($files.Count -ne $Recorded.Count) { throw "Input inventory count changed: $root ($($files.Count) != $($Recorded.Count))" }
        foreach ($file in $files) {
            $relative = Normalize-InputPath $file.Path
            if (-not $Recorded.ContainsKey($relative)) { throw "Unrecorded/new build input: $root\$relative" }
            $actual = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            if ($actual -ne $Recorded[$relative]) { throw "Frozen input hash mismatch: $root\$relative" }
        }
    }
}

function Read-RequiredText([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Evidence file missing: $Path" }
    Assert-NoReparsePath $Path
    return [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8)
}

function Get-CheckCount([string]$Text, [string]$Pattern, [string]$Name) {
    $summaries = [regex]::Matches($Text, $Pattern, [Text.RegularExpressions.RegexOptions]::Multiline)
    if ($summaries.Count -ne 1) { throw "Expected exactly one successful $Name summary, got $($summaries.Count)." }
    $count = [int]$summaries[0].Groups[1].Value
    if ($count -le 0) { throw "$Name summary has no checks." }
    return $count
}

function Read-SoftwareEvidence([string]$BuildStage, [string]$Configuration) {
    $buildLogPath = Join-Path $BuildStage "build-$Configuration.log"
    $buildLog = Read-RequiredText $buildLogPath
    $marker = '^PASS architecture=microkernel\+first-party-plugins extensibility=enabled\s*$'
    if ([regex]::Matches($buildLog, $marker, [Text.RegularExpressions.RegexOptions]::Multiline).Count -ne 1) {
        throw "Completed full-build marker missing/duplicated: $buildLogPath"
    }
    $logRoot = Join-Path $BuildStage "artifacts\tests\$Configuration\logs"
    $specifications = @(
        @{ Name = 'Core'; File = 'core-tests.log'; Pattern = '^TriLink.Core tests: checks=([0-9]+) failures=0\s*$' },
        @{ Name = 'Serial'; File = 'serial-lifecycle-tests.log'; Pattern = '^PASS serial lifecycle checks=([0-9]+)(?:\s|$)' },
        @{ Name = 'HardwareRoom'; File = 'hardware-room-tests.log'; Pattern = '^PASS hardware-room checks=([0-9]+)(?:\s|$)' },
        @{ Name = 'Modules'; File = 'modules-tests.log'; Pattern = '^PASS modules assertions=([0-9]+)(?:\s|$)' },
        @{ Name = 'Thunder'; File = 'thunder-tests.log'; Pattern = '^PASS thunder checks=([0-9]+)(?:\s|$)' }
    )
    $counts = [ordered]@{}
    foreach ($spec in $specifications) {
        $text = Read-RequiredText (Join-Path $logRoot $spec.File)
        $count = Get-CheckCount $text $spec.Pattern "$Configuration/$($spec.Name)"
        $fromBuild = Get-CheckCount $buildLog $spec.Pattern "$Configuration/full-build/$($spec.Name)"
        if ($count -ne $fromBuild) { throw "Suite/build-log count mismatch: $Configuration/$($spec.Name)" }
        $counts[$spec.Name] = $count
    }
    $desktop = Read-RequiredText (Join-Path $logRoot 'desktop-lifecycle.log')
    $desktopSummaries = @(
        'PASS mode guards with fake discovery; not physical USB verification',
        'PASS desktop lifecycle (automated form test, not interactive desktop acceptance)'
    )
    $desktopLines = @($desktop -split '\r?\n' | Where-Object { $_ -match '^PASS ' })
    foreach ($summary in $desktopSummaries) {
        if (@($desktopLines | Where-Object { $_ -eq $summary }).Count -ne 1) { throw "Desktop success summary missing/duplicated: $Configuration/$summary" }
    }
    # Desktop has no numeric summary. Count assertion PASS lines only, excluding
    # the two exact informational summaries above; do not count all build PASSs.
    $counts.Desktop = @($desktopLines | Where-Object { $_ -notin $desktopSummaries }).Count
    if ($counts.Desktop -le 0) { throw "No desktop assertions: $Configuration" }
    $counts.SmokeAndGuard = Get-CheckCount $buildLog '^PASS built-in-smoke assertions=([0-9]+)(?:\s|$)' "$Configuration/SmokeAndGuard"
    $total = 0
    foreach ($count in $counts.Values) { $total += [int]$count }
    $counts.TotalChecks = $total
    $counts.DesktopCountMethod = 'Assertion PASS lines, excluding the two exact desktop/mode informational summaries.'
    return [pscustomobject]$counts
}

function Copy-FileNew([string]$Source, [string]$Destination) {
    Assert-NoReparsePath $Source
    Assert-NoReparsePath $Destination
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Destination))
    [IO.File]::Copy($Source, $Destination, $false)
    if ((Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash) {
        throw "Copied evidence/runtime changed: $Destination"
    }
}

function Copy-DirectoryNew([string]$Source, [string]$Destination) {
    $sourceRoot = Resolve-Directory $Source
    if (Test-Path -LiteralPath $Destination) { throw "Refuse to replace directory: $Destination" }
    Assert-NoReparsePath $Destination
    $null = New-Item -ItemType Directory -Path $Destination
    foreach ($entry in Get-ChildItem -LiteralPath $sourceRoot -Force) {
        Assert-NoReparsePath $entry.FullName
        $target = Join-Path $Destination $entry.Name
        if ($entry.PSIsContainer) { Copy-DirectoryNew $entry.FullName $target }
        else { Copy-FileNew $entry.FullName $target }
    }
}

function Write-TextNew([string]$Path, [string]$Text) {
    Assert-NoReparsePath $Path
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $writer = New-Object IO.StreamWriter($stream, (New-Object Text.UTF8Encoding($false)))
    try { $writer.Write($Text) }
    finally { $writer.Dispose() }
}

function Invoke-ManifestCheck([string]$Runtime, [string]$Script, [string]$PowerShell) {
    $messages = @(& $PowerShell -NoProfile -NonInteractive -File $Script -Action Validate `
        -PluginRoot (Join-Path $Runtime 'plugins') -ProfilePath (Join-Path $Runtime 'profiles\desktop.profile.json') 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Plugin manifest/hash validation failed: $Runtime`n$($messages -join [Environment]::NewLine)" }
    return ($messages -join [Environment]::NewLine)
}

$project = Resolve-Directory $ProjectRoot
$stageRoot = Resolve-Directory $Stage
if ($stageRoot -eq $project) { throw 'Stage must be an isolated snapshot, not the live project.' }
$runtimeSource = Resolve-Directory (Join-Path $stageRoot 'artifacts\Release')
$frozenPath = Join-Path $stageRoot 'build-inputs.json'
$frozenHash = (Get-FileHash -LiteralPath $frozenPath -Algorithm SHA256).Hash
$frozen = (Read-RequiredText $frozenPath) | ConvertFrom-Json
if ($null -eq $frozen -or $null -eq $frozen.inputs -or @($frozen.inputs).Count -eq 0) { throw 'build-inputs.json has no input inventory.' }
$recorded = New-Object 'Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($inputRecord in @($frozen.inputs)) {
    $relative = Normalize-InputPath ([string]$inputRecord.path)
    $hash = [string]$inputRecord.sha256
    if ($hash -notmatch '^[0-9a-fA-F]{64}$' -or $recorded.ContainsKey($relative)) { throw "Invalid/duplicate frozen input: $relative" }
    $recorded.Add($relative, $hash)
}
Assert-FrozenInputs $project $stageRoot $recorded
$product = (Read-RequiredText (Join-Path $stageRoot 'VERSION')).Trim()
if ($product -ne ($Version + '-local') -or ([string]$frozen.version -ne $product)) { throw "Requested/frozen product version differs: requested=$Version frozen=$product" }
$imageVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $runtimeSource 'TriLink.MinClient.exe'))
if ($imageVersion.FileVersion -ne ($Version + '.0') -or $imageVersion.ProductVersion -ne $product) {
    throw "EXE version does not match VERSION: file=$($imageVersion.FileVersion) product=$($imageVersion.ProductVersion) expected=$product"
}
$softwareRelease = Read-SoftwareEvidence $stageRoot 'Release'
$softwareDebug = Read-SoftwareEvidence $stageRoot 'Debug'
foreach ($configuration in @('Release', 'Debug')) {
    foreach ($part in @('logs', 'ui', 'bin')) {
        $null = Resolve-Directory (Join-Path $stageRoot "artifacts\tests\$configuration\$part")
    }
}

$runtimeTarget = Join-Path $project ("artifacts\NodeConsole-$Version")
$packageTarget = Join-Path $project ("artifacts\packages\TriLink-NodeConsole-$Version-Release.zip")
$evidenceTarget = Join-Path $project ("artifacts\tests\NodeConsole-" + $Version.Replace('.', '') + '-UsbPlatform')
foreach ($target in @($runtimeTarget, $packageTarget, $evidenceTarget)) {
    Assert-NoReparsePath $target
    if (Test-Path -LiteralPath $target) { throw "Refuse to replace existing delivery: $target" }
}

$firmwareBuilt = $null
$firmwareReference = $null
if (-not [string]::IsNullOrWhiteSpace($FirmwareReceipt)) {
    $firmwarePath = (Resolve-Path -LiteralPath $FirmwareReceipt).Path
    $firmwareHash = (Get-FileHash -LiteralPath $firmwarePath -Algorithm SHA256).Hash
    $firmwareText = Read-RequiredText $firmwarePath
    $firmwareRecord = $firmwareText | ConvertFrom-Json
    if ($null -eq $firmwareRecord -or $firmwareRecord -is [Array]) { throw 'Firmware receipt must be a JSON object.' }
    $firmwareBuilt = $firmwareRecord.BuildVerification.Result -eq 'PASS' -and $firmwareRecord.Capacity.Result -eq 'PASS' `
        -and $firmwareRecord.HostTestsPassed -is [bool] -and $firmwareRecord.HostTestsPassed -eq $true `
        -and $firmwareRecord.FreshBuild -is [bool] -and $firmwareRecord.FreshBuild -eq $true
    if (-not $firmwareBuilt -or [string]$firmwareRecord.Version -ne $Version) {
        throw 'Firmware receipt does not record a fresh successful build/capacity/host-test result for the requested version.'
    }
    if ($firmwareRecord.Flashed -isnot [bool] -or $firmwareRecord.Flashed -ne $false `
        -or $firmwareRecord.HardwareVerified -isnot [bool] -or $firmwareRecord.HardwareVerified -ne $false) {
        throw 'This software-only package requires firmware evidence with Flashed=false and HardwareVerified=false.'
    }
    if ((Get-FileHash -LiteralPath $firmwarePath -Algorithm SHA256).Hash -ne $firmwareHash) { throw 'Firmware receipt changed while being read.' }
    $firmwareReference = [ordered]@{ path = $firmwarePath; sha256 = $firmwareHash;
        version = [string]$firmwareRecord.Version; buildClaim = $firmwareBuilt; verifiedByPackager = $false;
        buildResult = $firmwareRecord.BuildVerification.Result; capacityResult = $firmwareRecord.Capacity.Result;
        hostTestsPassed = $firmwareRecord.HostTestsPassed; freshBuild = $firmwareRecord.FreshBuild;
        flashed = $firmwareRecord.Flashed; hardwareVerified = $firmwareRecord.HardwareVerified;
        note = 'Receipt fields were checked; firmware compilation was not rerun by this packager. No image is included in the PC package.' }
}

$powerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path -LiteralPath $powerShell -PathType Leaf)) { throw 'Windows PowerShell is required for manifest validation.' }
$validator = Join-Path $stageRoot 'tools\pluginctl.ps1'
$sourceValidation = Invoke-ManifestCheck $runtimeSource $validator $powerShell
$sourceLinks = @(Get-ChildItem -LiteralPath $runtimeSource -Filter '*.lnk' -File)
if ($sourceLinks.Count -ne 3) { throw "Expected three local shortcuts, got $($sourceLinks.Count)." }
$shortcutShell = New-Object -ComObject WScript.Shell
$shortcuts = @($sourceLinks | ForEach-Object {
    [pscustomobject]@{ name = $_.Name; arguments = [string]$shortcutShell.CreateShortcut($_.FullName).Arguments }
})
foreach ($arguments in @('', '--demo --plugins-view', '--safe-mode')) {
    if (@($shortcuts | Where-Object { $_.arguments -eq $arguments }).Count -ne 1) { throw "Missing/duplicate shortcut arguments: $arguments" }
}
$portable = @()
foreach ($file in Get-ChildItem -LiteralPath $runtimeSource -Recurse -File -Force | Sort-Object FullName) {
    $relative = $file.FullName.Substring($runtimeSource.Length + 1)
    if ($relative -match '^module-data[\\/]' -or $file.Extension -eq '.lnk') { continue }
    Assert-NoReparsePath $file.FullName
    $portable += [pscustomobject]@{ path = $relative; source = $file.FullName; bytes = $file.Length;
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
}
if ($portable.Count -ne 22) { throw "Expected 22 portable runtime files (no shortcuts/module-data), got $($portable.Count)." }
$layoutMessages = @(& $powerShell -NoProfile -NonInteractive -File (Join-Path $stageRoot 'tools\check-release-layout.ps1') -Configuration Release 2>&1)
if ($LASTEXITCODE -ne 0) { throw "Source runtime layout check failed: $($layoutMessages -join [Environment]::NewLine)" }

# All gates above run before creating any delivery output.
Assert-FrozenInputs $project $stageRoot $recorded
if ((Get-FileHash -LiteralPath $frozenPath -Algorithm SHA256).Hash -ne $frozenHash) { throw 'build-inputs.json changed during preflight.' }
$null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($runtimeTarget))
$null = New-Item -ItemType Directory -Path $runtimeTarget
foreach ($file in $portable) {
    $destination = Join-Path $runtimeTarget $file.path
    Copy-FileNew $file.source $destination
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $file.sha256) { throw "Runtime changed since preflight: $($file.path)" }
}
foreach ($shortcut in $shortcuts) {
    $target = Join-Path $runtimeTarget $shortcut.name
    Copy-FileNew (Join-Path $runtimeSource $shortcut.name) $target
    $link = $shortcutShell.CreateShortcut($target)
    $link.TargetPath = Join-Path $runtimeTarget 'TriLink.MinClient.exe'
    $link.WorkingDirectory = $runtimeTarget
    $link.IconLocation = (Join-Path $runtimeTarget 'TriLink.MinClient.exe') + ',0'
    $link.Save()
    $check = $shortcutShell.CreateShortcut($target)
    if ($check.TargetPath -ne (Join-Path $runtimeTarget 'TriLink.MinClient.exe') -or $check.WorkingDirectory -ne $runtimeTarget -or $check.Arguments -ne $shortcut.arguments) {
        throw "Shortcut relocation/arguments differ: $($shortcut.name)"
    }
}
$deliveredValidation = Invoke-ManifestCheck $runtimeTarget $validator $powerShell
$null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($evidenceTarget))
$null = New-Item -ItemType Directory -Path $evidenceTarget
foreach ($configuration in @('Release', 'Debug')) {
    $destination = Join-Path $evidenceTarget $configuration
    $null = New-Item -ItemType Directory -Path $destination
    foreach ($part in @('logs', 'ui', 'bin')) {
        Copy-DirectoryNew (Join-Path $stageRoot "artifacts\tests\$configuration\$part") (Join-Path $destination $part)
    }
    Copy-FileNew (Join-Path $stageRoot "build-$configuration.log") (Join-Path $destination "build-$configuration.log")
}
Copy-FileNew $frozenPath (Join-Path $evidenceTarget 'build-inputs.json')
Write-TextNew (Join-Path $evidenceTarget 'plugin-validation.log') ($sourceValidation + [Environment]::NewLine + $deliveredValidation)

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
Assert-NoReparsePath $packageTarget
$null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($packageTarget))
$zipStream = [IO.File]::Open($packageTarget, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
$zip = New-Object IO.Compression.ZipArchive($zipStream, [IO.Compression.ZipArchiveMode]::Create, $false)
try {
    foreach ($file in $portable) {
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $runtimeTarget $file.path),
            $file.path.Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal)
    }
}
finally { $zip.Dispose(); $zipStream.Dispose() }
$expectedEntries = New-Object 'Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
foreach ($file in $portable) { $expectedEntries.Add($file.path.Replace('\', '/'), $file.sha256) }
$zip = [IO.Compression.ZipFile]::OpenRead($packageTarget)
try {
    if ($zip.Entries.Count -ne $portable.Count) { throw "ZIP file count differs: $($zip.Entries.Count)" }
    $seen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($entry in $zip.Entries) {
        if (-not $expectedEntries.ContainsKey($entry.FullName) -or -not $seen.Add($entry.FullName)) { throw "Unexpected/duplicate ZIP entry: $($entry.FullName)" }
        $stream = $entry.Open()
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $algorithm.Dispose() }
        if ($hash -ne $expectedEntries[$entry.FullName]) { throw "ZIP entry hash differs: $($entry.FullName)" }
    }
}
finally { $zip.Dispose() }
Assert-FrozenInputs $project $stageRoot $recorded
if ((Get-FileHash -LiteralPath $frozenPath -Algorithm SHA256).Hash -ne $frozenHash) { throw 'build-inputs.json changed during packaging.' }
foreach ($file in $portable) {
    foreach ($root in @($runtimeSource, $runtimeTarget)) {
        if ((Get-FileHash -LiteralPath (Join-Path $root $file.path) -Algorithm SHA256).Hash -ne $file.sha256) {
            throw "Runtime changed during packaging: $root\$($file.path)"
        }
    }
}
$runtimeHashes = @($portable | ForEach-Object { [ordered]@{ path = $_.path; bytes = $_.bytes; sha256 = $_.sha256 } })
$receipt = [ordered]@{
    generatedAt = [DateTimeOffset]::Now.ToOffset([TimeSpan]::FromHours(8)).ToString('o')
    product = $product; hostApi = '1.0'; runtime = $runtimeTarget; package = $packageTarget; stage = $stageRoot
    portableFileCount = $portable.Count; relocatedShortcutCount = $shortcuts.Count
    packageBytes = (Get-Item -LiteralPath $packageTarget).Length
    packageSha256 = (Get-FileHash -LiteralPath $packageTarget -Algorithm SHA256).Hash
    sourceInputCount = $recorded.Count; sourceInputs = @($frozen.inputs); runtimeFiles = $runtimeHashes
    buildInputsSha256 = $frozenHash
    software = [ordered]@{ Release = $softwareRelease; Debug = $softwareDebug;
        ReleaseChecks = $softwareRelease.TotalChecks; DebugChecks = $softwareDebug.TotalChecks
        evidence = 'Software/fake services, automated rendering and PC loopback only; not physical-device or interactive-desktop acceptance.' }
    softwareOnly = $true; fakeOnly = $true; hardwareTouched = $false; interactiveDesktopAccepted = $false
    firmwareBuilt = $firmwareBuilt; firmwareReceipt = $firmwareReference; firmwareImagesIncluded = $false
    firmwareFlashed = $false; oldRuntimeOverwritten = $false; userProcessesStoppedByAgent = $false; realClientStartedByPackager = $false
    copiedTimingClaims = $false; inputReadmesIncluded = $true
}
Write-TextNew (Join-Path $evidenceTarget 'delivery-receipt.json') ($receipt | ConvertTo-Json -Depth 12)
Write-Output "PASS frozen-inputs=$($recorded.Count) portable-files=$($portable.Count) relocated-shortcuts=$($shortcuts.Count)"
Write-Output "PASS software Release=$($softwareRelease.TotalChecks) Debug=$($softwareDebug.TotalChecks) (software/fake only)"
Write-Output "PASS package=$packageTarget SHA256=$($receipt.packageSha256) bytes=$($receipt.packageBytes)"
Write-Output "PASS runtime=$runtimeTarget receipt=$(Join-Path $evidenceTarget 'delivery-receipt.json')"
