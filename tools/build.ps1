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
        'trilink.game-link',
        'trilink.thunder',
        'trilink.desktop')]
    [string]$PluginId = 'all',

    [string]$CscPath,

    [string]$FrameworkPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($CscPath)) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        $installationPath = & $vswhere -latest -products '*' `
            -requires Microsoft.Component.MSBuild -property installationPath |
            Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($installationPath)) {
            $CscPath = Join-Path $installationPath 'MSBuild\Current\Bin\Roslyn\csc.exe'
        }
    }
}
if ([string]::IsNullOrWhiteSpace($CscPath) -or
    -not (Test-Path -LiteralPath $CscPath -PathType Leaf)) {
    throw 'Roslyn compiler not found. Install Visual Studio 2022 / Build Tools, or pass -CscPath <csc.exe>.'
}
if ([string]::IsNullOrWhiteSpace($FrameworkPath)) {
    $FrameworkPath = Join-Path ${env:ProgramFiles(x86)} `
        'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
}
foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll',
    'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $FrameworkPath $reference) -PathType Leaf)) {
        throw '.NET Framework 4.8 reference assemblies not found. Install the 4.8 Developer Pack, or pass -FrameworkPath <v4.8 directory>.'
    }
}
$csc = (Resolve-Path -LiteralPath $CscPath).Path
$framework = (Resolve-Path -LiteralPath $FrameworkPath).Path
$output = Join-Path $projectRoot "artifacts\$Configuration"
$pluginOutput = Join-Path $output 'plugins'
$profileOutput = Join-Path $output 'profiles'
$testRoot = Join-Path $projectRoot "artifacts\tests\$Configuration"
$testOutput = Join-Path $testRoot 'bin'
$uiOutput = Join-Path $testRoot 'ui'
$logOutput = Join-Path $testRoot 'logs'
$stagingOutput = Join-Path $projectRoot "artifacts\build\$Configuration\plugins"
$symbolOutput = Join-Path $projectRoot "artifacts\build\$Configuration\symbols"

$runtimeClientPath = [System.IO.Path]::GetFullPath((Join-Path $output 'TriLink.MinClient.exe'))
foreach ($runningClient in @(Get-Process -Name 'TriLink.MinClient' -ErrorAction SilentlyContinue)) {
    $runningClientPath = $runningClient.Path
    if (-not [string]::IsNullOrWhiteSpace($runningClientPath) -and
        [string]::Equals(
            [System.IO.Path]::GetFullPath($runningClientPath),
            $runtimeClientPath,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "当前 $Configuration 客户端仍在运行（PID $($runningClient.Id)）。请先从托盘菜单退出客户端，再重新构建；关闭窗口不会退出程序。"
    }
}

Write-Host "TOOLCHAIN compiler=$csc"
Write-Host "TOOLCHAIN framework=$framework"

$null = New-Item -ItemType Directory -Path $output -Force
$null = New-Item -ItemType Directory -Path $pluginOutput -Force
$null = New-Item -ItemType Directory -Path $profileOutput -Force
$null = New-Item -ItemType Directory -Path $testOutput -Force
$null = New-Item -ItemType Directory -Path $uiOutput -Force
$null = New-Item -ItemType Directory -Path $logOutput -Force
$null = New-Item -ItemType Directory -Path $stagingOutput -Force
if ($Configuration -eq 'Debug') {
    $null = New-Item -ItemType Directory -Path $symbolOutput -Force
}

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
    $debugOptions = @()
    if ($Configuration -eq 'Debug') {
        $pdbPath = Join-Path $symbolOutput ([System.IO.Path]::GetFileNameWithoutExtension($OutputPath) + '.pdb')
        $debugOptions = @('/debug:portable', "/pdb:$pdbPath")
    }
    & $csc @commonOptions @debugOptions @referenceOptions "/target:$Target" "/out:$OutputPath" @Sources
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
        'trilink.game-link' { Compile-GameLinkPlugin }
        'trilink.thunder' { Compile-ThunderPlugin }
        default { throw "Unknown plugin id: $Id" }
    }
}

function Compile-GameLinkPlugin {
    $sourceDirectory = Join-Path $projectRoot 'src\Plugins\TriLink.Plugin.GameLink'
    Deploy-Plugin 'trilink.game-link' 'TriLink.Plugin.GameLink.dll' $sourceDirectory `
        (Get-SourceFiles $sourceDirectory) @($abstractions)
}

function Compile-ThunderPlugin {
    $sourceDirectory = Join-Path $projectRoot 'src\Plugins\TriLink.Plugin.Thunder'
    Deploy-Plugin 'trilink.thunder' 'TriLink.Plugin.Thunder.dll' $sourceDirectory `
        (Get-SourceFiles $sourceDirectory) `
        @($abstractions, (Join-Path $framework 'System.Drawing.dll'), (Join-Path $framework 'System.Windows.Forms.dll'))
}

function Invoke-ThunderTests {
    $tests = Join-Path $testOutput 'TriLink.Thunder.Tests.exe'
    $gameAssembly = Join-Path $pluginOutput 'trilink.thunder\TriLink.Plugin.Thunder.dll'
    $linkAssembly = Join-Path $pluginOutput 'trilink.game-link\TriLink.Plugin.GameLink.dll'
    Invoke-Compile 'TriLink.Thunder.Tests' 'exe' $tests `
        (Get-SourceFiles (Join-Path $projectRoot 'tests\TriLink.Thunder.Tests')) `
        @($abstractions, $pluginHost, $gameAssembly, $linkAssembly,
            (Join-Path $framework 'System.Windows.Forms.dll'), (Join-Path $framework 'System.Drawing.dll'))
    foreach ($dependency in @($abstractions, $pluginHost, $gameAssembly, $linkAssembly)) {
        Copy-Item -LiteralPath $dependency -Destination $testOutput -Force
    }
    & $tests $output $uiOutput | Tee-Object -FilePath (Join-Path $logOutput 'thunder-tests.log')
    if ($LASTEXITCODE -ne 0) { throw "Thunder tests failed with exit code $LASTEXITCODE" }
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
        -OutputDirectory $testOutput -AbstractionsPath $abstractions `
        -CscPath $csc -FrameworkPath $framework |
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
    if ($PluginId -in @('trilink.game-link', 'trilink.thunder')) { Invoke-ThunderTests }
    Invoke-PluginValidation
    Invoke-BuiltInSmokeTests
    Invoke-UiSmoke 'ui-plugin-update-smoke.png' @('--plugins-view')
    if ($PluginId -in @('trilink.desktop', 'trilink.modules', 'trilink.text-tools', 'trilink.game-link', 'trilink.thunder')) {
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
Compile-GameLinkPlugin
Compile-ThunderPlugin
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
Invoke-ThunderTests

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
