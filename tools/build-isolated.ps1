[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [ValidateSet('Release','Debug','Both')][string]$Configuration = 'Both'
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }
$project = (Resolve-Path -LiteralPath $ProjectRoot).Path
$stage = Join-Path ([IO.Path]::GetTempPath()) ('tl0160-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
if (Test-Path -LiteralPath $stage) { throw "Stage already exists: $stage" }
$null = New-Item -ItemType Directory -Path $stage
$inputs = @()
foreach ($part in @('src','tests','tools','VERSION')) {
    $source = Join-Path $project $part
    Copy-Item -LiteralPath $source -Destination (Join-Path $stage $part) -Recurse
    $files = if (Test-Path -LiteralPath $source -PathType Container) {
        Get-ChildItem -LiteralPath $source -Recurse -File
    } else { Get-Item -LiteralPath $source }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($project.Length + 1)
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $stage $relative) -Algorithm SHA256).Hash) {
            throw "Copy changed while freezing source: $relative"
        }
        $inputs += [ordered]@{path=$relative;sha256=$hash}
    }
}
[ordered]@{project=$project;stage=$stage;version=(Get-Content (Join-Path $stage 'VERSION') -Raw).Trim();inputs=$inputs} |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage 'build-inputs.json') -Encoding UTF8
Write-Host "ISOLATED_STAGE=$stage"
$configurations = if ($Configuration -eq 'Both') { @('Release','Debug') } else { @($Configuration) }
$shell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
foreach ($item in $configurations) {
    & $shell -NoProfile -NonInteractive -File (Join-Path $stage 'tools\build.ps1') -Configuration $item 2>&1 |
        Tee-Object -FilePath (Join-Path $stage ('build-' + $item + '.log'))
    if ($LASTEXITCODE -ne 0) { throw "Isolated $item build failed; retained stage=$stage" }
}
Write-Host "PASS isolated-stage=$stage configurations=$($configurations -join ',')"
