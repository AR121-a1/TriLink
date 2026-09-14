[CmdletBinding()]
param(
    [ValidateSet('List', 'Validate')]
    [string]$Action = 'Validate',

    [string]$PluginRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\Release\plugins'),

    [string]$ProfilePath
)

$ErrorActionPreference = 'Stop'
$resolvedRoot = [System.IO.Path]::GetFullPath($PluginRoot)
if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
    throw "Plugin root not found: $resolvedRoot"
}

if ([string]::IsNullOrWhiteSpace($ProfilePath)) {
    $ProfilePath = Join-Path (Split-Path -Parent $resolvedRoot) 'profiles\desktop.profile.json'
}
$resolvedProfile = [System.IO.Path]::GetFullPath($ProfilePath)
if (-not (Test-Path -LiteralPath $resolvedProfile -PathType Leaf)) {
    throw "Plugin profile not found: $resolvedProfile"
}

try {
    $profile = Get-Content -LiteralPath $resolvedProfile -Raw | ConvertFrom-Json
}
catch {
    throw "Invalid JSON profile $resolvedProfile : $($_.Exception.Message)"
}
if ([int]$profile.schemaVersion -ne 1 -or
    [string]::IsNullOrWhiteSpace([string]$profile.name) -or
    ([string]$profile.name -notmatch '^[a-z0-9]+(?:[.-][a-z0-9]+)*$')) {
    throw "Invalid plugin profile header: $resolvedProfile"
}
$profileIds = @{}
foreach ($pluginIdValue in @($profile.plugins)) {
    $pluginId = [string]$pluginIdValue
    if ([string]::IsNullOrWhiteSpace($pluginId) -or
        $pluginId -notmatch '^[a-z0-9]+(?:[.-][a-z0-9]+)*$' -or
        $profileIds.ContainsKey($pluginId)) {
        throw "Invalid or duplicate plugin in profile $($profile.name): $pluginId"
    }
    $profileIds[$pluginId] = $true
}
if ($profileIds.Count -eq 0) {
    throw "Plugin profile is empty: $($profile.name)"
}

$records = @()
foreach ($directory in Get-ChildItem -LiteralPath $resolvedRoot -Directory | Sort-Object Name) {
    $manifestPath = Join-Path $directory.FullName 'plugin.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        continue
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "Invalid JSON manifest $manifestPath : $($_.Exception.Message)"
    }

    $records += [pscustomobject]@{
        Directory = $directory.FullName
        ManifestPath = $manifestPath
        Manifest = $manifest
    }
}

if ($records.Count -eq 0) {
    throw "No plugin manifests found in $resolvedRoot"
}

if ($Action -eq 'List') {
    $records | ForEach-Object {
        [pscustomobject]@{
            Id = $_.Manifest.id
            Version = $_.Manifest.version
            ActiveInProfile = [bool]$_.Manifest.enabled -and
                $profileIds.ContainsKey([string]$_.Manifest.id)
            Assembly = $_.Manifest.entryAssembly
            Provides = ($_.Manifest.providesServices -join ',')
            Requires = ($_.Manifest.requiresServices -join ',')
            Capabilities = ($_.Manifest.capabilities -join ',')
        }
    } | Format-Table -AutoSize
    return
}

$byId = @{}
foreach ($record in $records) {
    $manifest = $record.Manifest
    if ([int]$manifest.schemaVersion -ne 1) {
        throw "Unsupported schemaVersion in $($record.ManifestPath)"
    }
    if ([string]::IsNullOrWhiteSpace([string]$manifest.id) -or
        ([string]$manifest.id -notmatch '^[a-z0-9]+(?:[.-][a-z0-9]+)*$')) {
        throw "Invalid plugin id in $($record.ManifestPath)"
    }
    if ($byId.ContainsKey([string]$manifest.id)) {
        throw "Duplicate plugin id: $($manifest.id)"
    }
    if ([string]$manifest.hostApi -ne '1.0') {
        throw "Plugin $($manifest.id) requires unsupported host API $($manifest.hostApi)"
    }

    [System.Version]$parsedVersion = $null
    if (-not [System.Version]::TryParse([string]$manifest.version, [ref]$parsedVersion)) {
        throw "Invalid version for plugin $($manifest.id): $($manifest.version)"
    }

    $entryAssembly = [string]$manifest.entryAssembly
    if ([string]::IsNullOrWhiteSpace($entryAssembly) -or
        -not $entryAssembly.EndsWith('.dll', [System.StringComparison]::OrdinalIgnoreCase) -or
        [System.IO.Path]::GetFileName($entryAssembly) -ne $entryAssembly) {
        throw "Unsafe entryAssembly for plugin $($manifest.id)"
    }
    $assemblyPath = Join-Path $record.Directory $entryAssembly
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "Plugin assembly missing for $($manifest.id): $entryAssembly"
    }
    $expectedHash = [string]$manifest.sha256
    if ($expectedHash -notmatch '^[0-9a-fA-F]{64}$') {
        throw "Invalid SHA-256 for plugin $($manifest.id)"
    }
    $actualHash = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) {
        throw "SHA-256 mismatch for plugin $($manifest.id)"
    }
    if ([string]::IsNullOrWhiteSpace([string]$manifest.entryType)) {
        throw "Plugin $($manifest.id) has no entryType"
    }

    foreach ($propertyName in @('requiresServices', 'providesServices')) {
        $seenServices = @{}
        foreach ($serviceValue in @($manifest.$propertyName)) {
            $service = [string]$serviceValue
            if ([string]::IsNullOrWhiteSpace($service) -or
                $service -notmatch '^[a-z0-9]+(?:[.-][a-z0-9]+)*$') {
                throw "Invalid $propertyName entry in plugin $($manifest.id): $service"
            }
            if ($seenServices.ContainsKey($service)) {
                throw "Duplicate $propertyName entry in plugin $($manifest.id): $service"
            }
            $seenServices[$service] = $true
        }
    }

    $byId[[string]$manifest.id] = $record
}

foreach ($profileId in @($profileIds.Keys)) {
    if (-not $byId.ContainsKey($profileId)) {
        throw "Profile $($profile.name) selects missing plugin $profileId"
    }
}

$enabled = @{}
foreach ($record in $records) {
    if ([bool]$record.Manifest.enabled -and
        $profileIds.ContainsKey([string]$record.Manifest.id)) {
        $enabled[[string]$record.Manifest.id] = $record
    }
}

$serviceProviders = @{}
foreach ($record in @($enabled.Values)) {
    foreach ($serviceValue in @($record.Manifest.providesServices)) {
        $service = [string]$serviceValue
        if ($serviceProviders.ContainsKey($service)) {
            throw "Service $service has multiple providers: $($serviceProviders[$service].Manifest.id), $($record.Manifest.id)"
        }
        $serviceProviders[$service] = $record
    }
}
$hostServices = @{
    'trilink.plugin-catalog' = $true
}

$visitState = @{}
$ordered = New-Object System.Collections.Generic.List[string]
function Visit-Plugin([string]$Id) {
    if ($visitState.ContainsKey($Id)) {
        if ([int]$visitState[$Id] -eq 1) {
            throw "Plugin dependency cycle includes: $Id"
        }
        if ([int]$visitState[$Id] -eq 2) {
            return
        }
    }

    $visitState[$Id] = 1
    $record = $enabled[$Id]
    $seenDependencies = @{}
    foreach ($dependencyValue in @($record.Manifest.dependencies)) {
        $dependency = [string]$dependencyValue
        if ([string]::IsNullOrWhiteSpace($dependency) -or $dependency -notmatch '^[a-z0-9]+(?:[.-][a-z0-9]+)*$') {
            throw "Invalid dependency in plugin $Id"
        }
        if ($seenDependencies.ContainsKey($dependency)) {
            throw "Duplicate dependency $dependency in plugin $Id"
        }
        $seenDependencies[$dependency] = $true
        if (-not $enabled.ContainsKey($dependency)) {
            throw "Plugin $Id requires missing or disabled plugin $dependency"
        }
        Visit-Plugin $dependency
    }

    foreach ($serviceValue in @($record.Manifest.requiresServices)) {
        $service = [string]$serviceValue
        if ($hostServices.ContainsKey($service)) {
            continue
        }
        if (-not $serviceProviders.ContainsKey($service)) {
            throw "Plugin $Id requires missing service $service"
        }
        Visit-Plugin ([string]$serviceProviders[$service].Manifest.id)
    }

    $visitState[$Id] = 2
    $ordered.Add($Id)
}

foreach ($id in @($enabled.Keys | Sort-Object)) {
    Visit-Plugin $id
}

Write-Host "PASS profile=$($profile.name) plugins=$($enabled.Count) manifests=$($records.Count) api=1.0"
Write-Host "PASS load-order=$($ordered -join ' -> ')"
