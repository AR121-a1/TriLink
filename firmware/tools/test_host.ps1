[CmdletBinding()]
param(
    [string]$Compiler,
    [string]$EvidenceDirectory
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Compiler)) {
    $compilerCommand = Get-Command gcc.exe -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $compilerCommand) {
        throw 'Host GCC was not found. Pass -Compiler with a GCC executable path.'
    }
    $Compiler = $compilerCommand.Source
} else {
    $compilerCommand = Get-Command $Compiler -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $compilerCommand) {
        $Compiler = $compilerCommand.Source
    }
}

if (-not (Test-Path -LiteralPath $Compiler -PathType Leaf)) {
    throw "Host compiler not found: $Compiler"
}

$compilerTarget = (& $Compiler -dumpmachine 2>&1 | Out-String).Trim()
if (($LASTEXITCODE -ne 0) -or ($compilerTarget -notmatch '(?:mingw|windows)')) {
    throw "Host compiler must target Windows, got: $compilerTarget"
}

$outputDirectory = Join-Path $projectDirectory 'build_host_tests'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

function Invoke-HostTest {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string[]]$Sources,

        [Parameter(Mandatory)]
        [string[]]$IncludeDirectories
    )

    foreach ($requiredInput in @($Sources + $IncludeDirectories)) {
        if (-not (Test-Path -LiteralPath $requiredInput)) {
            throw "Host test input is missing: $requiredInput"
        }
    }

    $outputExecutable = Join-Path $outputDirectory (
        '{0}-{1}.exe' -f $Name, [guid]::NewGuid().ToString('N'))
    $arguments = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-pedantic')
    foreach ($includeDirectory in $IncludeDirectories) {
        $arguments += "-I$includeDirectory"
    }
    $arguments += $Sources
    $arguments += @('-o', $outputExecutable)

    try {
        & $Compiler @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$Name compilation failed with exit code $LASTEXITCODE"
        }
        if (-not (Test-Path -LiteralPath $outputExecutable -PathType Leaf)) {
            throw "Host compiler returned success without creating: $outputExecutable"
        }
        if ((Get-Item -LiteralPath $outputExecutable).Length -le 0) {
            throw "Host compiler created an empty executable: $outputExecutable"
        }

        & $outputExecutable
        if ($LASTEXITCODE -ne 0) {
            throw "$Name failed with exit code $LASTEXITCODE"
        }
        if ($Name -eq 'trilink_bridge_tests' -and -not [string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
            $fixture = @(& $outputExecutable --caps-fixture)
            if ($LASTEXITCODE -ne 0) { throw 'Node CAPS fixture failed.' }
            $fixture | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'node-caps-fixture.txt') -Encoding UTF8
        }
    }
    finally {
        if (Test-Path -LiteralPath $outputExecutable -PathType Leaf) {
            Remove-Item -LiteralPath $outputExecutable -Force
        }
    }
}

$coreDirectory = Join-Path $projectDirectory 'components\wakenic_core'
$protocolDirectory = Join-Path $projectDirectory 'components\trilink_protocol'
$clusterDirectory = Join-Path $projectDirectory 'components\trilink_cluster'
$testDirectory = Join-Path $projectDirectory 'tests\host'

Invoke-HostTest -Name 'wakenic_state_tests' -Sources @(
    (Join-Path $coreDirectory 'wakenic_state.c'),
    (Join-Path $testDirectory 'test_wakenic_state.c')
) -IncludeDirectories @(
    (Join-Path $coreDirectory 'include')
)

Invoke-HostTest -Name 'trilink_protocol_tests' -Sources @(
    (Join-Path $protocolDirectory 'trilink_protocol.c'),
    (Join-Path $testDirectory 'test_trilink_protocol.c')
) -IncludeDirectories @(
    (Join-Path $protocolDirectory 'include')
)

Invoke-HostTest -Name 'trilink_service_tests' -Sources @(
    (Join-Path $protocolDirectory 'trilink_protocol.c'),
    (Join-Path $protocolDirectory 'trilink_service.c'),
    (Join-Path $testDirectory 'test_trilink_service.c')
) -IncludeDirectories @(
    (Join-Path $protocolDirectory 'include')
)

Invoke-HostTest -Name 'trilink_content_tests' -Sources @(
    (Join-Path $protocolDirectory 'trilink_protocol.c'),
    (Join-Path $protocolDirectory 'trilink_service.c'),
    (Join-Path $protocolDirectory 'trilink_content.c'),
    (Join-Path $testDirectory 'test_trilink_content.c')
) -IncludeDirectories @(
    (Join-Path $protocolDirectory 'include')
)

Invoke-HostTest -Name 'trilink_file_wire_tests' -Sources @(
    (Join-Path $protocolDirectory 'trilink_protocol.c'),
    (Join-Path $protocolDirectory 'trilink_service.c'),
    (Join-Path $protocolDirectory 'trilink_content.c'),
    (Join-Path $protocolDirectory 'trilink_file_wire.c'),
    (Join-Path $testDirectory 'test_trilink_file_wire.c')
) -IncludeDirectories @(
    (Join-Path $protocolDirectory 'include')
)

Invoke-HostTest -Name 'trilink_cluster_tests' -Sources @(
    (Join-Path $protocolDirectory 'trilink_protocol.c'),
    (Join-Path $clusterDirectory 'trilink_cluster.c'),
    (Join-Path $testDirectory 'test_trilink_cluster.c')
) -IncludeDirectories @(
    (Join-Path $protocolDirectory 'include'),
    (Join-Path $clusterDirectory 'include')
)

Invoke-HostTest -Name 'trilink_control_tests' -Sources @(
    (Join-Path $protocolDirectory 'trilink_control.c'),
    (Join-Path $testDirectory 'test_trilink_control.c')
) -IncludeDirectories @(
    (Join-Path $protocolDirectory 'include')
)
Invoke-HostTest -Name 'trilink_route_tests' -Sources @(
    (Join-Path $protocolDirectory 'trilink_protocol.c'),
    (Join-Path $protocolDirectory 'trilink_service.c'),
    (Join-Path $protocolDirectory 'trilink_route.c'),
    (Join-Path $testDirectory 'test_trilink_route.c')
) -IncludeDirectories @(
    (Join-Path $protocolDirectory 'include')
)
Invoke-HostTest -Name 'trilink_route_control_tests' -Sources @(
    (Join-Path $protocolDirectory 'trilink_route_control.c'),
    (Join-Path $testDirectory 'test_trilink_route_control.c')
) -IncludeDirectories @(
    (Join-Path $protocolDirectory 'include')
)
Invoke-HostTest -Name 'trilink_app_tests' -Sources @(
    (Join-Path $projectDirectory 'components\trilink_node\trilink_app.c'),
    (Join-Path $testDirectory 'test_trilink_app.c')
) -IncludeDirectories @(
    (Join-Path $protocolDirectory 'include'),
    (Join-Path $projectDirectory 'components\trilink_node\include')
)
Invoke-HostTest -Name 'trilink_bridge_tests' -Sources @(
    (Join-Path $protocolDirectory 'trilink_room.c'),
    (Join-Path $protocolDirectory 'trilink_protocol.c'),
    (Join-Path $protocolDirectory 'trilink_service.c'),
    (Join-Path $protocolDirectory 'trilink_route.c'),
    (Join-Path $protocolDirectory 'trilink_route_control.c'),
    (Join-Path $projectDirectory 'components\trilink_node\trilink_bridge.c'),
    (Join-Path $projectDirectory 'components\trilink_node\trilink_app.c'),
    (Join-Path $testDirectory 'test_trilink_bridge.c')
) -IncludeDirectories @(
    (Join-Path $testDirectory 'fakes'),
    (Join-Path $protocolDirectory 'include'),
    (Join-Path $projectDirectory 'components\trilink_node\include')
)
Invoke-HostTest -Name 'trilink_room_tests' -Sources @(
    (Join-Path $protocolDirectory 'trilink_protocol.c'),
    (Join-Path $protocolDirectory 'trilink_service.c'),
    (Join-Path $protocolDirectory 'trilink_route.c'),
    (Join-Path $protocolDirectory 'trilink_room.c'),
    (Join-Path $testDirectory 'test_trilink_room.c')
) -IncludeDirectories @((Join-Path $protocolDirectory 'include'))
Invoke-HostTest -Name 'trilink_room_rgb_tests' -Sources @(
    (Join-Path $protocolDirectory 'trilink_room.c'),
    (Join-Path $protocolDirectory 'trilink_protocol.c'),
    (Join-Path $protocolDirectory 'trilink_service.c'),
    (Join-Path $protocolDirectory 'trilink_route.c'),
    (Join-Path $protocolDirectory 'trilink_route_control.c'),
    (Join-Path $projectDirectory 'components\trilink_node\trilink_bridge.c'),
    (Join-Path $projectDirectory 'components\trilink_node\trilink_app.c'),
    (Join-Path $testDirectory 'test_trilink_room_rgb.c')
) -IncludeDirectories @(
    (Join-Path $testDirectory 'fakes'),
    (Join-Path $protocolDirectory 'include'),
    (Join-Path $projectDirectory 'components\trilink_node\include')
)
$ledDirectory = Join-Path $projectDirectory 'components\led_strip'
Invoke-HostTest -Name 'trilink_rgb_rmt_tests' -Sources @(
    (Join-Path $testDirectory 'test_trilink_rgb_rmt.c')
) -IncludeDirectories @(
    (Join-Path $testDirectory 'rgb_fakes'),
    (Join-Path $ledDirectory 'include'),
    (Join-Path $ledDirectory 'interface'),
    (Join-Path $ledDirectory 'src')
)
Invoke-HostTest -Name 'trilink_rgb_worker_tests' -Sources @(
    (Join-Path $testDirectory 'test_trilink_rgb_worker.c')
) -IncludeDirectories @(
    (Join-Path $testDirectory 'rgb_worker_fakes'),
    (Join-Path $projectDirectory 'components\trilink_node\include')
)
Invoke-HostTest -Name 'trilink_usb_mux_tests' -Sources @(
    (Join-Path $projectDirectory 'components\trilink_usb\trilink_usb_mux.c'),
    (Join-Path $testDirectory 'test_trilink_usb_mux.c')
) -IncludeDirectories @((Join-Path $projectDirectory 'components\trilink_usb\include'))
Invoke-HostTest -Name 'trilink_web_policy_tests' -Sources @(
    (Join-Path $projectDirectory 'components\trilink_web\trilink_web_policy.c'),
    (Join-Path $protocolDirectory 'trilink_control.c'),
    (Join-Path $protocolDirectory 'trilink_route_control.c'),
    (Join-Path $testDirectory 'test_web_policy.c')
) -IncludeDirectories @(
    (Join-Path $projectDirectory 'components\trilink_web\include'),
    (Join-Path $protocolDirectory 'include')
)
Write-Host 'PASS: all host test executables completed successfully.'
