[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $projectRoot "artifacts\$Configuration"
$client = Join-Path $output 'TriLink.MinClient.exe'

if (-not (Test-Path -LiteralPath $client -PathType Leaf)) {
    throw "TriLink client was not found: $client"
}

$shell = New-Object -ComObject WScript.Shell

function Write-TriLinkShortcut(
    [string]$Name,
    [string]$Arguments,
    [string]$Description
) {
    $path = Join-Path $output $Name
    $shortcut = $shell.CreateShortcut($path)
    $shortcut.TargetPath = $client
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = $output
    $shortcut.IconLocation = "$client,0"
    $shortcut.Description = $Description
    $shortcut.Save()

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Shortcut was not created: $path"
    }

    Write-Host "PASS shortcut=$path arguments=$Arguments"
}

Write-TriLinkShortcut `
    '启动 TriLink（三节点演示）.lnk' `
    '--demo --plugins-view' `
    '打开 TriLink 三节点模拟与插件状态界面'

Write-TriLinkShortcut `
    '启动 TriLink（真实硬件）.lnk' `
    '' `
    '打开 TriLink ESP32-S3 USB CDC 客户端'
