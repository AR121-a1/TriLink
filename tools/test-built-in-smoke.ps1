[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Run this .NET Framework regression with Windows PowerShell (powershell.exe).'
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$release = Join-Path $projectRoot "artifacts\$Configuration"
$buildPath = Join-Path $PSScriptRoot 'build.ps1'
$script:passed = 0

function Assert-Regression([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:passed++
    Write-Host "PASS $Message"
}

function Assert-BuiltInArguments([string[]]$Arguments) {
    $profileIndex = [Array]::IndexOf($Arguments, '--profile')
    Assert-Regression ($profileIndex -ge 0 -and $profileIndex + 1 -lt $Arguments.Length -and
        $Arguments[$profileIndex + 1] -eq 'desktop') 'smoke fixes its first profile option to desktop'
    Assert-Regression (@($Arguments | Where-Object { $_ -ieq '--safe-mode' }).Count -gt 0) `
        'smoke always selects built-ins with --safe-mode'
}

function Get-SelectionHashes {
    $result = @{}
    foreach ($name in @('desktop.json', 'desktop.json.previous')) {
        $path = Join-Path $release "module-data\$name"
        $result[$name] = if (Test-Path -LiteralPath $path -PathType Leaf) {
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        } else { '<absent>' }
    }
    return $result
}

$selectionBefore = Get-SelectionHashes
$tokens = $null
$parseErrors = $null
$buildAst = [System.Management.Automation.Language.Parser]::ParseFile($buildPath, [ref]$tokens, [ref]$parseErrors)
Assert-Regression ($parseErrors.Count -eq 0) 'build script parses without errors'
$smokeFunction = $buildAst.Find({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-UiSmoke'
}, $true)
Assert-Regression ($null -ne $smokeFunction) 'regression exercises the actual build smoke function'
$gateCalls = @($buildAst.FindAll({ param($node)
    $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Invoke-BuiltInSmokeTests'
}, $true))
Assert-Regression ($gateCalls.Count -eq 2) 'full and targeted builds both include the smoke-selection gate'
$desktopTestSource = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'tests\TriLink.Desktop.Tests\Program.cs'))
Assert-Regression ([regex]::IsMatch($desktopTestSource,
    'new\s+HostEnvironment\s*\(\s*release\s*,\s*new\[\]\s*\{[^}]*"--safe-mode"[^}]*\}\s*,\s*"desktop"\s*\)')) `
    'desktop lifecycle regression also fixes safe mode and the built-in desktop profile'

# Only the one function is evaluated. Never execute build.ps1 or start a client process.
. ([ScriptBlock]::Create($smokeFunction.Extent.Text))
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('trilink-built-in-smoke-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixtureRoot
$uiOutput = Join-Path $fixtureRoot 'ui'
$null = New-Item -ItemType Directory -Path $uiOutput
$client = Join-Path $fixtureRoot 'not-started.exe'
$script:capturedArguments = @()
$script:launchExitCode = 0
$script:writeScreenshot = $true
function Start-Process {
    [CmdletBinding()]
    param([string]$FilePath, [string[]]$ArgumentList, [string]$WindowStyle, [switch]$Wait, [switch]$PassThru)
    $script:capturedArguments = @($ArgumentList)
    if ($script:writeScreenshot) {
        $index = [Array]::IndexOf($ArgumentList, '--screenshot')
        # This marker only satisfies the mocked command gate; it is not a rendered image.
        [System.IO.File]::WriteAllText($ArgumentList[$index + 1], 'mocked launch; no GUI created')
    }
    return [pscustomobject]@{ ExitCode = $script:launchExitCode }
}

Invoke-UiSmoke 'full.png' @() 6> $null
Assert-BuiltInArguments $script:capturedArguments
Invoke-UiSmoke 'plugins.png' @('--plugins-view') 6> $null
Assert-BuiltInArguments $script:capturedArguments
Assert-Regression ($script:capturedArguments -contains '--plugins-view') 'plugin-view smoke retains its requested view'
Invoke-UiSmoke 'extra-profile.png' @('--profile', 'lab') 6> $null
Assert-BuiltInArguments $script:capturedArguments
$smokeArguments = @($script:capturedArguments)

# Verify the guard detects removal of the option, rather than merely mirroring an expected list.
$mutant = $smokeFunction.Extent.Text.Replace("function Invoke-UiSmoke(", "function Invoke-UnsafeSmokeFixture(")
$mutant = $mutant.Replace("'--safe-mode', ", '')
. ([ScriptBlock]::Create($mutant))
$null = Invoke-UnsafeSmokeFixture 'unsafe.png' @() 6> $null
$rejected = $false
try { Assert-BuiltInArguments $script:capturedArguments } catch { $rejected = $true }
Assert-Regression $rejected 'regression rejects a smoke command with safe mode removed'

$script:launchExitCode = 2
$rejected = $false
try { Invoke-UiSmoke 'failed.png' @() } catch { $rejected = $true }
Assert-Regression $rejected 'nonzero client exit fails the build smoke gate'
$script:launchExitCode = 0
$script:writeScreenshot = $false
[System.IO.File]::WriteAllText((Join-Path $uiOutput 'missing.png'), 'stale mocked screenshot')
$rejected = $false
try { Invoke-UiSmoke 'missing.png' @() } catch { $rejected = $true }
Assert-Regression $rejected 'missing fresh screenshot fails even when a stale screenshot existed'

Add-Type -AssemblyName System.Web.Extensions
$null = [System.Reflection.Assembly]::LoadFrom((Join-Path $release 'TriLink.Plugin.Abstractions.dll'))
$null = [System.Reflection.Assembly]::LoadFrom((Join-Path $release 'TriLink.PluginHost.dll'))

# Clone only manifests and the profile: no plugin DLL is loaded, copied or executed.
$runtimeRoot = Join-Path $fixtureRoot 'runtime'
$null = New-Item -ItemType Directory -Path (Join-Path $runtimeRoot 'profiles') -Force
Copy-Item -LiteralPath (Join-Path $release 'profiles\desktop.profile.json') -Destination (Join-Path $runtimeRoot 'profiles')
foreach ($directory in Get-ChildItem -LiteralPath (Join-Path $release 'plugins') -Directory) {
    $target = Join-Path $runtimeRoot "plugins\$($directory.Name)"
    $null = New-Item -ItemType Directory -Path $target -Force
    Copy-Item -LiteralPath (Join-Path $directory.FullName 'plugin.json') -Destination $target
}
$token = '0123456789abcdef0123456789abcdef'
$packageRoot = Join-Path $runtimeRoot "module-data\packages\$token"
$null = New-Item -ItemType Directory -Path $packageRoot -Force
$imported = Get-Content -LiteralPath (Join-Path $runtimeRoot 'plugins\trilink.text-tools\plugin.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$imported.version = '0.0.1'
$imported | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $packageRoot 'plugin.json') -Encoding UTF8
$profile = Get-Content -LiteralPath (Join-Path $runtimeRoot 'profiles\desktop.profile.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$selectionPath = Join-Path $runtimeRoot 'module-data\desktop.json'
@{ schemaVersion = 1; enabled = @($profile.plugins); packages = @{ 'trilink.text-tools' = $token } } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $selectionPath -Encoding UTF8
$fixtureSelectionHash = (Get-FileHash -LiteralPath $selectionPath -Algorithm SHA256).Hash
$environment = [TriLink.PluginHost.HostEnvironment]::new($runtimeRoot, [string[]]$smokeArguments, 'desktop')
$store = [TriLink.PluginHost.ModuleStore]::new($environment)
$normal = @($store.Resolve($store.ReadSelection($false)))
$smokeSafeMode = @($environment.Arguments | Where-Object { $_ -ieq '--safe-mode' }).Count -gt 0
$safe = @($store.Resolve($store.ReadSelection($smokeSafeMode)))
$normalTool = @($normal | Where-Object { $_.id -eq 'trilink.text-tools' })[0]
$safeTool = @($safe | Where-Object { $_.id -eq 'trilink.text-tools' })[0]
Assert-Regression ($normalTool.SourceDirectory -eq $packageRoot -and $normalTool.version -eq '0.0.1') `
    'ordinary selection uses the retained same-ID imported version'
Assert-Regression ($safeTool.SourceDirectory -eq (Join-Path $runtimeRoot 'plugins\trilink.text-tools')) `
    'safe smoke selects the rebuilt built-in package despite a same-ID override'
$safeEnabledIds = @($safe | Where-Object { $_.enabled } | ForEach-Object { $_.id })
Assert-Regression (@(Compare-Object -ReferenceObject @($profile.plugins) -DifferenceObject $safeEnabledIds).Count -eq 0) `
    'safe smoke enables the complete built-in desktop profile'
Assert-Regression ((Get-FileHash -LiteralPath $selectionPath -Algorithm SHA256).Hash -eq $fixtureSelectionHash) `
    'normal and safe selection leave the retained configuration unchanged'
[System.IO.File]::WriteAllText($selectionPath, '{ corrupt overlay')
$rejected = $false
try { $null = $store.ReadSelection($false) } catch { $rejected = $true }
Assert-Regression $rejected 'ordinary selection rejects a corrupt user overlay'
Assert-Regression ($store.ReadSelection($true).packages.Count -eq 0) 'safe smoke ignores even a corrupt user overlay'
$selectionAfter = Get-SelectionHashes
Assert-Regression (@($selectionBefore.Keys | Where-Object { $selectionBefore[$_] -ne $selectionAfter[$_] }).Count -eq 0) `
    'real release selection and previous backup are unchanged'
Write-Host "PASS built-in-smoke assertions=$script:passed (TEMP metadata and mocked launch only; no GUI/serial)"
Write-Host "PASS fixture=$fixtureRoot"
