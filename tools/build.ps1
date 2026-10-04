[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet(
        'all',
        'trilink.rooms',
        'trilink.serial',
        'trilink.simulation',
        'trilink.modules',
        'trilink.text-tools',
        'trilink.hardware-room',
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

    $manifest = Get-Content -LiteralPath (Join-Path $SourceDirectory 'plugin.json') -Raw -Encoding UTF8 |
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
        @($abstractions)
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

function Compile-ModulesPlugin {
    $sourceDirectory = Join-Path $projectRoot 'src\Plugins\TriLink.Plugin.Modules'
    Deploy-Plugin 'trilink.modules' 'TriLink.Plugin.Modules.dll' $sourceDirectory `
        (Get-SourceFiles $sourceDirectory) `
        @($abstractions, $pluginHost, (Join-Path $framework 'System.Windows.Forms.dll'))
}

function Compile-TextToolsPlugin {
    $sourceDirectory = Join-Path $projectRoot 'src\Plugins\TriLink.Plugin.TextTools'
    Deploy-Plugin 'trilink.text-tools' 'TriLink.Plugin.TextTools.dll' $sourceDirectory `
        (Get-SourceFiles $sourceDirectory) `
        @($abstractions, (Join-Path $framework 'System.Windows.Forms.dll'), (Join-Path $framework 'System.Drawing.dll'))
}

function Compile-HardwareRoomPlugin {
    $sourceDirectory = Join-Path $projectRoot 'src\Plugins\TriLink.Plugin.HardwareRoom'
    Deploy-Plugin 'trilink.hardware-room' 'TriLink.Plugin.HardwareRoom.dll' $sourceDirectory `
        (Get-SourceFiles $sourceDirectory) `
        @($abstractions, (Join-Path $framework 'System.Drawing.dll'), (Join-Path $framework 'System.Windows.Forms.dll'))
}

function Invoke-PluginSelection([string]$Id) {
    switch ($Id) {
        'trilink.rooms' { Compile-RoomsPlugin }
        'trilink.serial' { Compile-SerialPlugin }
        'trilink.simulation' { Compile-SimulationPlugin }
        'trilink.desktop' { Compile-DesktopPlugin }
        'trilink.modules' { Compile-ModulesPlugin }
        'trilink.text-tools' { Compile-TextToolsPlugin }
        'trilink.hardware-room' { Compile-HardwareRoomPlugin }
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

function Invoke-BuiltInSmokeTests {
    $windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path -LiteralPath $windowsPowerShell -PathType Leaf)) {
        throw 'Windows PowerShell is required for the .NET Framework smoke-selection regression.'
    }
    & $windowsPowerShell -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'test-built-in-smoke.ps1') `
        -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Built-in smoke selection regression failed with exit code $LASTEXITCODE"
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

    # This gate verifies the freshly built packages, independent of retained user overrides.
    $arguments = @('--profile', 'desktop', '--safe-mode', '--screenshot', $screenshot) + $ExtraArguments
    $uiProcess = Start-Process -FilePath $client `
        -ArgumentList $arguments `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($uiProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $screenshot)) {
        $detail = if (Test-Path -LiteralPath $errorFile) {
            Get-Content -LiteralPath $errorFile -Raw -Encoding UTF8
        }
        else {
            'No startup error file was produced.'
        }
        throw "UI smoke render failed with exit code $($uiProcess.ExitCode): $detail"
    }

    Write-Host "PASS ui=$screenshot profile=desktop selection=built-ins (--safe-mode)"
}

function Invoke-DesktopLifecycleTest {
    $desktopTests = Join-Path $testOutput 'TriLink.Desktop.Tests.exe'
    Invoke-Compile `
        'TriLink.Desktop.Tests' `
        'exe' `
        $desktopTests `
        (Get-SourceFiles (Join-Path $projectRoot 'tests\TriLink.Desktop.Tests')) `
        @($abstractions, $pluginHost, (Join-Path $framework 'System.Windows.Forms.dll'),
            (Join-Path $framework 'System.Drawing.dll'))

    foreach ($dependency in @($abstractions, $pluginHost)) {
        Copy-Item -LiteralPath $dependency -Destination $testOutput -Force
    }

    & $desktopTests $output | Tee-Object -FilePath (Join-Path $logOutput 'desktop-lifecycle.log')
    if ($LASTEXITCODE -ne 0) {
        throw "Desktop lifecycle test failed with exit code $LASTEXITCODE"
    }
}

function Invoke-CoreTests {
    $tests = Join-Path $testOutput 'TriLink.Core.Tests.exe'
    $roomsAssembly = Join-Path $pluginOutput 'trilink.rooms\TriLink.Plugin.Rooms.dll'
    $serialAssembly = Join-Path $pluginOutput 'trilink.serial\TriLink.Plugin.Serial.dll'
    Invoke-Compile `
        'TriLink.Core.Tests' `
        'exe' `
        $tests `
        (Get-SourceFiles (Join-Path $projectRoot 'tests\TriLink.Core.Tests')) `
        @($abstractions, $pluginHost, $roomsAssembly, $serialAssembly,
            (Join-Path $framework 'System.Web.Extensions.dll'))
    foreach ($dependency in @($abstractions, $pluginHost, $roomsAssembly, $serialAssembly)) {
        Copy-Item -LiteralPath $dependency -Destination $testOutput -Force
    }
    & $tests | Tee-Object -FilePath (Join-Path $logOutput 'core-tests.log')
    if ($LASTEXITCODE -ne 0) { throw "Core tests failed with exit code $LASTEXITCODE" }
}

function Invoke-SerialLifecycleTests {
    & (Join-Path $projectRoot 'tests\TriLink.SerialLifecycle.Tests\run.ps1') `
        -OutputDirectory $testOutput -AbstractionsPath $abstractions |
        Tee-Object -FilePath (Join-Path $logOutput 'serial-lifecycle-tests.log')
    if ($LASTEXITCODE -ne 0) {
        throw "Serial lifecycle tests failed with exit code $LASTEXITCODE"
    }
}

function Invoke-HardwareRoomTests {
    $tests = Join-Path $testOutput 'TriLink.HardwareRoom.Tests.exe'
    $assembly = Join-Path $pluginOutput 'trilink.hardware-room\TriLink.Plugin.HardwareRoom.dll'
    Invoke-Compile 'TriLink.HardwareRoom.Tests' 'exe' $tests `
        (Get-SourceFiles (Join-Path $projectRoot 'tests\TriLink.HardwareRoom.Tests')) `
        @($abstractions, $assembly, (Join-Path $framework 'System.Windows.Forms.dll'), (Join-Path $framework 'System.Drawing.dll'))
    foreach ($dependency in @($abstractions, $assembly)) { Copy-Item -LiteralPath $dependency -Destination $testOutput -Force }
    & $tests (Join-Path $uiOutput 'ui-hardware-room-fixture.png') | Tee-Object -FilePath (Join-Path $logOutput 'hardware-room-tests.log')
    if ($LASTEXITCODE -ne 0) { throw "Hardware Room tests failed with exit code $LASTEXITCODE" }
}

function Invoke-ModuleTests {
    Invoke-Compile 'TriLink.ModuleProbe' 'exe' (Join-Path $testOutput 'TriLink.ModuleProbe.exe') `
        (Get-SourceFiles (Join-Path $projectRoot 'tests\TriLink.ModuleProbe')) `
        @($abstractions, $pluginHost)
    $moduleTests = Join-Path $testOutput 'TriLink.Modules.Tests.exe'
    $modulesAssembly = Join-Path $pluginOutput 'trilink.modules\TriLink.Plugin.Modules.dll'
    $textToolsAssembly = Join-Path $pluginOutput 'trilink.text-tools\TriLink.Plugin.TextTools.dll'
    Invoke-Compile 'TriLink.Modules.Tests' 'exe' $moduleTests `
        (Get-SourceFiles (Join-Path $projectRoot 'tests\TriLink.Modules.Tests')) `
        @($abstractions, $pluginHost, $modulesAssembly, $textToolsAssembly,
            (Join-Path $framework 'System.Web.Extensions.dll'),
            (Join-Path $framework 'System.Windows.Forms.dll'), (Join-Path $framework 'System.Drawing.dll'))
    foreach ($dependency in @($abstractions, $pluginHost, $modulesAssembly, $textToolsAssembly)) {
        Copy-Item -LiteralPath $dependency -Destination $testOutput -Force
    }
    & $moduleTests $output | Tee-Object -FilePath (Join-Path $logOutput 'modules-tests.log')
    if ($LASTEXITCODE -ne 0) { throw "Module tests failed with exit code $LASTEXITCODE" }
}

if ($PluginId -ne 'all') {
    if (-not (Test-Path -LiteralPath $abstractions -PathType Leaf)) {
        throw 'Run a full build once before building an individual plugin.'
    }
    if (-not (Test-Path -LiteralPath $client -PathType Leaf)) {
        throw 'Host executable is missing; run a full build first.'
    }

    Invoke-PluginSelection $PluginId
    if ($PluginId -in @('trilink.serial', 'trilink.rooms')) { Invoke-CoreTests }
    if ($PluginId -eq 'trilink.serial') { Invoke-SerialLifecycleTests }
    if ($PluginId -eq 'trilink.hardware-room') { Invoke-HardwareRoomTests }
    Invoke-PluginValidation
    Invoke-BuiltInSmokeTests
    Invoke-UiSmoke 'ui-plugin-update-smoke.png' @('--plugins-view')
    if ($PluginId -in @('trilink.desktop', 'trilink.modules', 'trilink.text-tools')) {
        Invoke-ModuleTests
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
Compile-ModulesPlugin
Compile-TextToolsPlugin
Compile-HardwareRoomPlugin
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

Invoke-CoreTests
Invoke-SerialLifecycleTests
Invoke-HardwareRoomTests
Invoke-ModuleTests

Invoke-PluginValidation
Invoke-BuiltInSmokeTests
Invoke-UiSmoke 'ui-smoke.png' @()
Invoke-UiSmoke 'ui-plugins.png' @('--plugins-view')
Invoke-DesktopLifecycleTest
& (Join-Path $projectRoot 'tools\create-launch-shortcuts.ps1') `
    -Configuration $Configuration
& (Join-Path $PSScriptRoot 'check-release-layout.ps1') -Configuration $Configuration

Write-Host "PASS client=$client"
Write-Host 'PASS architecture=microkernel+first-party-plugins extensibility=enabled'
