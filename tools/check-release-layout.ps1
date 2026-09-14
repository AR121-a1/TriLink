[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtime = Join-Path $projectRoot "artifacts\$Configuration"
$requiredFiles = @(
    'TriLink.MinClient.exe',
    'TriLink.Plugin.Abstractions.dll',
    'TriLink.PluginHost.dll',
    '启动 TriLink（三节点演示）.lnk',
    '启动 TriLink（真实硬件）.lnk'
)
$requiredDirectories = @('plugins', 'profiles')

foreach ($name in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $runtime $name) -PathType Leaf)) {
        throw "Missing runtime file: $name"
    }
}
foreach ($name in $requiredDirectories) {
    if (-not (Test-Path -LiteralPath (Join-Path $runtime $name) -PathType Container)) {
        throw "Missing runtime directory: $name"
    }
}
foreach ($entry in Get-ChildItem -LiteralPath $runtime -Force) {
    $allowed = if ($entry.PSIsContainer) { $requiredDirectories } else { $requiredFiles }
    if ($entry.Name -notin $allowed) {
        throw "Non-runtime entry in client directory; archive it before delivery: $($entry.FullName)"
    }
}

foreach ($entry in Get-ChildItem -LiteralPath (Join-Path $runtime 'profiles') -Force) {
    if ($entry.PSIsContainer -or $entry.Name -notmatch '^[a-z0-9.-]+\.profile\.json$') {
        throw "Unexpected profile entry: $($entry.FullName)"
    }
}
foreach ($plugin in Get-ChildItem -LiteralPath (Join-Path $runtime 'plugins') -Force) {
    if (-not $plugin.PSIsContainer) {
        throw "Unexpected entry in plugins directory: $($plugin.FullName)"
    }
    $manifestPath = Join-Path $plugin.FullName 'plugin.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $allowed = @('plugin.json', [string]$manifest.entryAssembly)
    foreach ($entry in Get-ChildItem -LiteralPath $plugin.FullName -Force) {
        if ($entry.PSIsContainer -or $entry.Name -notin $allowed) {
            throw "Non-runtime entry in plugin: $($entry.FullName)"
        }
    }
}

Write-Host "PASS clean-runtime=$runtime (application, dependencies, plugins, profiles, launch shortcuts only)"
