$ErrorActionPreference = 'Stop'
$repositoryPath = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repositoryPath
$applicationPath = Join-Path $repositoryPath 'artifacts\ProxyGAyer\ProxyGAyer.dll'
if (-not (Test-Path -LiteralPath $applicationPath)) { throw 'Build first with .\build.ps1.' }
if (Get-Process -Name ProxyGAyer -ErrorAction SilentlyContinue) { throw 'Close ProxyGAyer before refreshing its screenshots.' }
$captureDirectory = Join-Path $repositoryPath ('.build\readme-captures\' + [guid]::NewGuid().ToString('N'))
dotnet $applicationPath --preview $captureDirectory
if ($LASTEXITCODE -ne 0) { throw 'App preview failed.' }
$assetDirectory = Join-Path $repositoryPath 'docs\assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
$captures = @{ 'preview.png' = 'app-overview.png'; 'batch.png' = 'app-batch.png'; 'advanced.png' = 'app-advanced.png' }
foreach ($capture in $captures.GetEnumerator()) {
    Copy-Item -LiteralPath (Join-Path $captureDirectory $capture.Key) -Destination (Join-Path $assetDirectory $capture.Value)
}
Write-Output 'Refreshed three real app screenshots using isolated demo settings.'
