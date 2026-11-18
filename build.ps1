param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build\dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
if (-not $env:NUGET_PACKAGES) {
    $env:NUGET_PACKAGES = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages'
}
if (-not $SkipTests) {
    $testDirectory = Join-Path $PSScriptRoot ('.build\tests\' + [guid]::NewGuid().ToString('N'))
    dotnet run --project tests/ProxyGAyer.Tests -c Release -- $testDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
dotnet publish src/ProxyGAyer -c Release --no-self-contained -o artifacts/ProxyGAyer --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
# Keep the release guide self-contained; GitHub artwork stays in the repository.
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\PACKAGE.md') -Destination (Join-Path $PSScriptRoot 'artifacts\ProxyGAyer\README.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE.txt') -Destination (Join-Path $PSScriptRoot 'artifacts\ProxyGAyer\LICENSE.txt')
$publishFiles = (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'artifacts\ProxyGAyer') -File).FullName
Compress-Archive -LiteralPath $publishFiles -DestinationPath (Join-Path $PSScriptRoot 'artifacts\ProxyGAyer-win-x64.zip') -Force
Write-Output 'Ready: artifacts\ProxyGAyer\ProxyGAyer.exe (requires .NET 10 Desktop Runtime)'
