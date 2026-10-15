[CmdletBinding()]
param(
    [string]$IdfProfile,
    [string]$BuildDirectory = 'build',
    [string]$SdkconfigPath,
    [switch]$NoCcache,
    [switch]$RequireFresh
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($env:PROCESSOR_ARCHITECTURE)) {
    if ([Environment]::Is64BitOperatingSystem) {
        $env:PROCESSOR_ARCHITECTURE = 'AMD64'
    } else {
        $env:PROCESSOR_ARCHITECTURE = 'x86'
    }
}

if (-not [string]::IsNullOrWhiteSpace($IdfProfile)) {
    if (-not (Test-Path -LiteralPath $IdfProfile)) {
        throw "ESP-IDF activation profile not found: $IdfProfile"
    }
    . $IdfProfile
}

if ((-not $env:IDF_PATH) -or (-not $env:IDF_PYTHON_ENV_PATH)) {
    throw 'ESP-IDF is not active. Pass -IdfProfile with the PowerShell activation profile path.'
}

# Some sandboxed Windows processes contain both PATH and Path entries. The IDF
# profile updates one while Python/CMake can read the stale duplicate, hiding
# Ninja and the toolchain. Recreate one canonical process-level Path entry.
$idfProcessPath = $env:PATH
Remove-Item Env:Path -ErrorAction SilentlyContinue
$env:Path = $idfProcessPath

# Trust only the selected, read-only SDK for this child build process. Do not
# change the user's global Git config or use safe.directory=*.
$gitConfigIndex = 0
if ($env:GIT_CONFIG_COUNT) { $gitConfigIndex = [int]$env:GIT_CONFIG_COUNT }
Set-Item -Path "Env:GIT_CONFIG_KEY_$gitConfigIndex" -Value 'safe.directory'
Set-Item -Path "Env:GIT_CONFIG_VALUE_$gitConfigIndex" -Value ($env:IDF_PATH -replace '\\', '/')
$env:GIT_CONFIG_COUNT = [string]($gitConfigIndex + 1)

$idfPython = Join-Path $env:IDF_PYTHON_ENV_PATH 'Scripts\python.exe'
$idfScript = Join-Path $env:IDF_PATH 'tools\idf.py'
foreach ($idfEntryPoint in @($idfPython, $idfScript)) {
    if (-not (Test-Path -LiteralPath $idfEntryPoint -PathType Leaf)) {
        throw "ESP-IDF entry point is missing: $idfEntryPoint"
    }
}

$idfVersion = (& $idfPython $idfScript --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "idf.py --version failed with exit code $LASTEXITCODE"
}
if ($idfVersion -ne 'ESP-IDF v5.5.5') {
    throw "Expected ESP-IDF v5.5.5, got: $idfVersion"
}

foreach ($tool in @('cmake.exe', 'ninja.exe', 'xtensa-esp32s3-elf-gcc.exe')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "Required ESP-IDF tool is unavailable: $tool"
    }
}

if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    throw 'BuildDirectory must not be empty.'
}

if (-not [IO.Path]::IsPathRooted($BuildDirectory)) {
    $BuildDirectory = Join-Path $projectDirectory $BuildDirectory
}
$BuildDirectory = [IO.Path]::GetFullPath($BuildDirectory)
$normalizedProjectDirectory = [IO.Path]::GetFullPath($projectDirectory).TrimEnd('\', '/')
$normalizedBuildDirectory = $BuildDirectory.TrimEnd('\', '/')
$buildLeaf = Split-Path -Leaf $normalizedBuildDirectory
if ($buildLeaf -notmatch '^build(?:[._-].*)?$') {
    throw "BuildDirectory leaf must be 'build' or start with 'build.', 'build_' or 'build-': $BuildDirectory"
}
if ($normalizedProjectDirectory.Equals(
        $normalizedBuildDirectory,
        [StringComparison]::OrdinalIgnoreCase) -or
    $normalizedProjectDirectory.StartsWith(
        $normalizedBuildDirectory + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "BuildDirectory must not be the project directory or one of its ancestors: $BuildDirectory"
}

$idfArguments = @('-B', $BuildDirectory)
if ($NoCcache) {
    $idfArguments += '--no-ccache'
}

if (-not [string]::IsNullOrWhiteSpace($SdkconfigPath)) {
    if (-not [IO.Path]::IsPathRooted($SdkconfigPath)) {
        $SdkconfigPath = Join-Path $projectDirectory $SdkconfigPath
    }
    $SdkconfigPath = [IO.Path]::GetFullPath($SdkconfigPath)
    $sdkconfigLeaf = Split-Path -Leaf $SdkconfigPath
    if ($sdkconfigLeaf -notmatch '^sdkconfig(?:[._-].*)?$' -or
        $sdkconfigLeaf -match '\.defaults(?:\.|$)') {
        throw "SdkconfigPath must name an sdkconfig output, not a source/defaults file: $SdkconfigPath"
    }
    $defaultsPath = [IO.Path]::GetFullPath(
        (Join-Path $projectDirectory 'sdkconfig.defaults'))
    if ($SdkconfigPath.Equals($defaultsPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "SdkconfigPath must not overwrite sdkconfig.defaults: $SdkconfigPath"
    }
    if ($SdkconfigPath.StartsWith(
            $normalizedBuildDirectory + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "SdkconfigPath must be outside BuildDirectory: $SdkconfigPath"
    }

    if ($RequireFresh -and (Test-Path -LiteralPath $SdkconfigPath)) {
        throw "RequireFresh expected a missing sdkconfig: $SdkconfigPath"
    }
    $sdkconfigParent = Split-Path -Parent $SdkconfigPath
    $idfArguments += @(
        '-D', "SDKCONFIG=$SdkconfigPath",
        '-D', "SDKCONFIG_DEFAULTS=$projectDirectory\sdkconfig.defaults"
    )
} elseif ($RequireFresh) {
    throw 'RequireFresh requires an explicit -SdkconfigPath.'
}

if ($RequireFresh -and (Test-Path -LiteralPath $BuildDirectory)) {
    throw "RequireFresh expected a missing build directory: $BuildDirectory"
}
if ($RequireFresh) {
    $projectPrefix = $normalizedProjectDirectory + [IO.Path]::DirectorySeparatorChar
    if ($normalizedBuildDirectory.StartsWith(
            $projectPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "RequireFresh requires BuildDirectory outside the project: $BuildDirectory"
    }
    if ($SdkconfigPath.StartsWith(
            $projectPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "RequireFresh requires SdkconfigPath outside the project: $SdkconfigPath"
    }
}

# This is the first filesystem write performed by the script.
if (-not [string]::IsNullOrWhiteSpace($SdkconfigPath)) {
    New-Item -ItemType Directory -Path $sdkconfigParent -Force | Out-Null
}

Push-Location -LiteralPath $projectDirectory
try {
    Write-Host "Building esp32s3_wakenic_p2p with $idfVersion"
    Write-Host "Build directory: $BuildDirectory"
    & $idfPython $idfScript @idfArguments build
    if ($LASTEXITCODE -ne 0) {
        throw "idf.py build failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
