[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet(
        'all',
        'trilink.rooms',
        'trilink.serial',
        'trilink.simulation',
        'trilink.desktop')]
    [string]$PluginId = 'all'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$framework = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$output = Join-Path $projectRoot "artifacts\$Configuration"
$pluginOutput = Join-Path $output 'plugins'
$profileOutput = Join-Path $output 'profiles'
$testRoot = Join-Path $projectRoot "artifacts\tests\$Configuration"
$testOutput = Join-Path $testRoot 'bin'
$uiOutput = Join-Path $testRoot 'ui'
$logOutput = Join-Path $testRoot 'logs'
$stagingOutput = Join-Path $projectRoot "artifacts\build\$Configuration\plugins"

if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) {
    throw "Roslyn compiler not found at verified path: $csc"
}
if (-not (Test-Path -LiteralPath $framework -PathType Container)) {
    throw ".NET Framework 4.8 reference assemblies not found: $framework"
}

$null = New-Item -ItemType Directory -Path $output -Force
$null = New-Item -ItemType Directory -Path $pluginOutput -Force
$null = New-Item -ItemType Directory -Path $profileOutput -Force
$null = New-Item -ItemType Directory -Path $testOutput -Force
$null = New-Item -ItemType Directory -Path $uiOutput -Force
$null = New-Item -ItemType Directory -Path $logOutput -Force
$null = New-Item -ItemType Directory -Path $stagingOutput -Force

$commonReferences = @(
    (Join-Path $framework 'mscorlib.dll'),
    (Join-Path $framework 'System.dll'),
    (Join-Path $framework 'System.Core.dll')
)
$commonOptions = @(
    '/nologo',
    '/noconfig',
    '/nostdlib+',
    '/langversion:latest',
    '/warnaserror+',
    '/deterministic+',
    '/utf8output',
    $(if ($Configuration -eq 'Release') { '/optimize+' } else { '/optimize-' })
)

function Get-SourceFiles([string]$Directory, [bool]$Recurse = $false) {
    $arguments = @{
        LiteralPath = $Directory
        Filter = '*.cs'
        File = $true
    }
    if ($Recurse) {
        $arguments.Recurse = $true
    }

    return @(Get-ChildItem @arguments | Sort-Object FullName | ForEach-Object { $_.FullName })
}

function Invoke-Compile(
    [string]$Name,
    [string]$Target,
    [string]$OutputPath,
    [string[]]$Sources,
    [string[]]$References
) {
    if ($Sources.Count -eq 0) {
        throw "No source files supplied for $Name"
    }

    $referenceOptions = @($commonReferences + $References | Select-Object -Unique |
        ForEach-Object { "/reference:$_" })
    & $csc @commonOptions @referenceOptions "/target:$Target" "/out:$OutputPath" @Sources
    if ($LASTEXITCODE -ne 0) {
        throw "$Name compilation failed with exit code $LASTEXITCODE"
    }
}

$abstractions = Join-Path $output 'TriLink.Plugin.Abstractions.dll'
$pluginHost = Join-Path $output 'TriLink.PluginHost.dll'
$client = Join-Path $output 'TriLink.MinClient.exe'

function Compile-Abstractions {
    $sources = Get-SourceFiles (Join-Path $projectRoot 'src\TriLink.Plugin.Abstractions')
    Invoke-Compile `
        'TriLink.Plugin.Abstractions' `
        'library' `
        $abstractions `
        $sources `
        @((Join-Path $framework 'System.Windows.Forms.dll'))
}

function Compile-PluginHost {
    Invoke-Compile `
        'TriLink.PluginHost' `
        'library' `
        $pluginHost `
        (Get-SourceFiles (Join-Path $projectRoot 'src\TriLink.PluginHost')) `
        @(
            $abstractions,
            (Join-Path $framework 'System.Web.Extensions.dll')
        )
}

function Deploy-Plugin(
    [string]$Id,
    [string]$AssemblyName,
    [string]$SourceDirectory,
    [string[]]$Sources,
    [string[]]$References
) {
    $stagingDirectory = Join-Path $stagingOutput $Id
    $destinationDirectory = Join-Path $pluginOutput $Id
    $null = New-Item -ItemType Directory -Path $stagingDirectory -Force
    $null = New-Item -ItemType Directory -Path $destinationDirectory -Force
    $stagedAssembly = Join-Path $stagingDirectory $AssemblyName
    Invoke-Compile $Id 'library' $stagedAssembly $Sources $References

    $destinationAssembly = Join-Path $destinationDirectory $AssemblyName
    $destinationManifest = Join-Path $destinationDirectory 'plugin.json'
    Copy-Item -LiteralPath $stagedAssembly -Destination $destinationAssembly -Force

    $manifest = Get-Content -LiteralPath (Join-Path $SourceDirectory 'plugin.json') -Raw |
        ConvertFrom-Json
    $hash = (Get-FileHash -LiteralPath $destinationAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest | Add-Member -NotePropertyName sha256 -NotePropertyValue $hash -Force
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $destinationManifest -Encoding UTF8
    Write-Host "PASS plugin=$Id assembly=$AssemblyName"
}

function Compile-RoomsPlugin {
    $sourceDirectory = Join-Path $projectRoot 'src\Plugins\TriLink.Plugin.Rooms'
    Deploy-Plugin `
        'trilink.rooms' `
        'TriLink.Plugin.Rooms.dll' `
        $sourceDirectory `
        (Get-SourceFiles $sourceDirectory) `
        @($abstractions)
}

function Compile-SerialPlugin {
    $sourceDirectory = Join-Path $projectRoot 'src\Plugins\TriLink.Plugin.Serial'
    Deploy-Plugin `
        'trilink.serial' `
        'TriLink.Plugin.Serial.dll' `
        $sourceDirectory `
        (Get-SourceFiles $sourceDirectory) `
        @(
            $abstractions,
            (Join-Path $framework 'System.Management.dll')
        )
}

function Compile-SimulationPlugin {
    $sourceDirectory = Join-Path $projectRoot 'src\Plugins\TriLink.Plugin.Simulation'
    Deploy-Plugin `
        'trilink.simulation' `
        'TriLink.Plugin.Simulation.dll' `
        $sourceDirectory `
        @((Join-Path $sourceDirectory 'SimulationPlugin.cs')) `
        @($abstractions)
}

function Compile-DesktopPlugin {
    $sourceDirectory = Join-Path $projectRoot 'src\Plugins\TriLink.Plugin.Desktop'
    Deploy-Plugin `
        'trilink.desktop' `
        'TriLink.Plugin.Desktop.dll' `
        $sourceDirectory `
        (Get-SourceFiles $sourceDirectory) `
        @(
            $abstractions,
            (Join-Path $framework 'System.Drawing.dll'),
            (Join-Path $framework 'System.Windows.Forms.dll')
        )
}

function Invoke-PluginSelection([string]$Id) {
    switch ($Id) {
        'trilink.rooms' { Compile-RoomsPlugin }
        'trilink.serial' { Compile-SerialPlugin }
        'trilink.simulation' { Compile-SimulationPlugin }
        'trilink.desktop' { Compile-DesktopPlugin }
        default { throw "Unknown plugin id: $Id" }
    }
}

function Invoke-PluginValidation {
    & (Join-Path $PSScriptRoot 'pluginctl.ps1') `
        -Action Validate `
        -PluginRoot $pluginOutput `
        -ProfilePath (Join-Path $profileOutput 'desktop.profile.json')
    if ($LASTEXITCODE -ne 0) {
        throw "Plugin manifest validation failed with exit code $LASTEXITCODE"
    }
}

function Invoke-UiSmoke([string]$Name, [string[]]$ExtraArguments) {
    $screenshot = Join-Path $uiOutput $Name
    $errorFile = $screenshot + '.error.txt'
    if (Test-Path -LiteralPath $screenshot) {
        Remove-Item -LiteralPath $screenshot -Force
    }
    if (Test-Path -LiteralPath $errorFile) {
        Remove-Item -LiteralPath $errorFile -Force
    }

    $arguments = @('--screenshot', $screenshot) + $ExtraArguments
    $uiProcess = Start-Process -FilePath $client `
        -ArgumentList $arguments `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($uiProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $screenshot)) {
        $detail = if (Test-Path -LiteralPath $errorFile) {
            Get-Content -LiteralPath $errorFile -Raw
        }
        else {
            'No startup error file was produced.'
        }
        throw "UI smoke render failed with exit code $($uiProcess.ExitCode): $detail"
    }

    Write-Host "PASS ui=$screenshot"
}

function Invoke-DesktopLifecycleTest {
    $desktopTests = Join-Path $testOutput 'TriLink.Desktop.Tests.exe'
    Invoke-Compile `
        'TriLink.Desktop.Tests' `
        'exe' `
        $desktopTests `
        (Get-SourceFiles (Join-Path $projectRoot 'tests\TriLink.Desktop.Tests')) `
        @($abstractions, $pluginHost, (Join-Path $framework 'System.Windows.Forms.dll'))

    foreach ($dependency in @($abstractions, $pluginHost)) {
        Copy-Item -LiteralPath $dependency -Destination $testOutput -Force
    }

    & $desktopTests $output | Tee-Object -FilePath (Join-Path $logOutput 'desktop-lifecycle.log')
    if ($LASTEXITCODE -ne 0) {
        throw "Desktop lifecycle test failed with exit code $LASTEXITCODE"
    }
}

if ($PluginId -ne 'all') {
    if (-not (Test-Path -LiteralPath $abstractions -PathType Leaf)) {
        throw 'Run a full build once before building an individual plugin.'
    }
    if (-not (Test-Path -LiteralPath $client -PathType Leaf)) {
        throw 'Host executable is missing; run a full build first.'
    }

    Invoke-PluginSelection $PluginId
    Invoke-PluginValidation
    Invoke-UiSmoke 'ui-plugin-update-smoke.png' @('--plugins-view')
    if ($PluginId -eq 'trilink.desktop') {
        Invoke-DesktopLifecycleTest
    }
    & (Join-Path $PSScriptRoot 'check-release-layout.ps1') -Configuration $Configuration
    Write-Host "PASS targeted-update=$PluginId"
    return
}

Compile-Abstractions
Compile-PluginHost
Compile-RoomsPlugin
Compile-SerialPlugin
Compile-SimulationPlugin
Compile-DesktopPlugin
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\profiles\desktop.profile.json') `
    -Destination (Join-Path $profileOutput 'desktop.profile.json') `
    -Force

Invoke-Compile `
    'TriLink.MinClient' `
    'winexe' `
    $client `
    (Get-SourceFiles (Join-Path $projectRoot 'src\TriLink.MinClient')) `
    @(
        $abstractions,
        $pluginHost,
        (Join-Path $framework 'System.Windows.Forms.dll')
    )

$tests = Join-Path $testOutput 'TriLink.Core.Tests.exe'
$roomsAssembly = Join-Path $pluginOutput 'trilink.rooms\TriLink.Plugin.Rooms.dll'
$serialAssembly = Join-Path $pluginOutput 'trilink.serial\TriLink.Plugin.Serial.dll'
Invoke-Compile `
    'TriLink.Core.Tests' `
    'exe' `
    $tests `
    (Get-SourceFiles (Join-Path $projectRoot 'tests\TriLink.Core.Tests')) `
    @(
        $abstractions,
        $pluginHost,
        $roomsAssembly,
        $serialAssembly,
        (Join-Path $framework 'System.Web.Extensions.dll')
    )

foreach ($dependency in @($abstractions, $pluginHost, $roomsAssembly, $serialAssembly)) {
    Copy-Item -LiteralPath $dependency -Destination $testOutput -Force
}

& $tests | Tee-Object -FilePath (Join-Path $logOutput 'core-tests.log')
if ($LASTEXITCODE -ne 0) {
    throw "Core tests failed with exit code $LASTEXITCODE"
}

Invoke-PluginValidation
Invoke-UiSmoke 'ui-smoke.png' @()
Invoke-UiSmoke 'ui-plugins.png' @('--plugins-view')
Invoke-DesktopLifecycleTest
& (Join-Path $projectRoot 'tools\create-launch-shortcuts.ps1') `
    -Configuration $Configuration
& (Join-Path $PSScriptRoot 'check-release-layout.ps1') -Configuration $Configuration

Write-Host "PASS client=$client"
Write-Host 'PASS architecture=microkernel+first-party-plugins extensibility=enabled'
