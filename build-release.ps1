param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string]$Version = '1.0.0',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build\dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:MSBuildEnableWorkloadResolver = 'false'
if (-not $env:NUGET_PACKAGES) {
    $env:NUGET_PACKAGES = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages'
}

if (-not $SkipTests) {
    $testDirectory = Join-Path $PSScriptRoot ('.build\tests\' + [guid]::NewGuid().ToString('N'))
    dotnet run --project tests/ProxyGAyer.Tests -c Release -- $testDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed. No release was packaged.' }
}

# Every run gets a new staging directory so old DLLs cannot enter the package.
$stagingDirectory = Join-Path $PSScriptRoot ('.build\releases\' + $Version + '-' + [guid]::NewGuid().ToString('N'))
$releaseDirectory = Join-Path $PSScriptRoot ('artifacts\releases\v' + $Version)
New-Item -ItemType Directory -Path $stagingDirectory,$releaseDirectory -Force | Out-Null

foreach ($variant in @('light','standalone')) {
    $publishDirectory = Join-Path $stagingDirectory $variant
    $selfContained = if ($variant -eq 'standalone') { 'true' } else { 'false' }
    $publishArguments = @(
        'publish', 'src/ProxyGAyer/ProxyGAyer.csproj', '-c', 'Release', '-r', 'win-x64',
        '--self-contained', $selfContained, '-p:PublishSingleFile=true', '-p:PublishTrimmed=false',
        '-p:DebugType=None', '-p:DebugSymbols=false', '-p:CopyOutputSymbolsToPublishDirectory=false',
        ('-p:Version=' + $Version), '--source', 'https://api.nuget.org/v3/index.json',
        '-o', $publishDirectory, '--nologo'
    )
    if ($variant -eq 'standalone') {
        $publishArguments += '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true'
    }
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw ('Publish failed: ' + $variant) }
    $fileName = if ($variant -eq 'standalone') {
        'ProxyGAyer-v' + $Version + '-standalone-win-x64.exe'
    } else { 'ProxyGAyer-v' + $Version + '-win-x64.exe' }
    Copy-Item -LiteralPath (Join-Path $publishDirectory 'ProxyGAyer.exe') -Destination (Join-Path $releaseDirectory $fileName)
}

$lightExecutable = Join-Path $releaseDirectory ('ProxyGAyer-v' + $Version + '-win-x64.exe')
$standaloneExecutable = Join-Path $releaseDirectory ('ProxyGAyer-v' + $Version + '-standalone-win-x64.exe')
$packageDirectory = Join-Path $stagingDirectory 'package'
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
Copy-Item -LiteralPath $lightExecutable -Destination (Join-Path $packageDirectory 'ProxyGAyer.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE.txt') -Destination (Join-Path $packageDirectory 'LICENSE.txt')
$quickGuide = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'docs\releases\README.txt') -Raw -Encoding utf8
$quickGuide = [regex]::Replace($quickGuide, '^ProxyGAyer v[^\r\n]+', ('ProxyGAyer v' + $Version + ' | Windows 10/11 x64'))
Set-Content -LiteralPath (Join-Path $packageDirectory 'README.txt') -Value $quickGuide -Encoding utf8
Copy-Item -LiteralPath (Join-Path $packageDirectory 'README.txt') -Destination (Join-Path $releaseDirectory 'README.txt')
$archivePath = Join-Path $releaseDirectory ('ProxyGAyer-v' + $Version + '-win-x64.zip')
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $packageDirectory -File).FullName -DestinationPath $archivePath -Force

$notesSource = Join-Path $PSScriptRoot ('docs\releases\v' + $Version + '.md')
$notesDestination = Join-Path $releaseDirectory 'RELEASE_NOTES.md'
if (Test-Path -LiteralPath $notesSource) {
    Copy-Item -LiteralPath $notesSource -Destination $notesDestination
} else {
    @"
ProxyGAyer v$Version for Windows 10/11 x64.

- Standalone EXE: includes .NET; no adjacent DLLs needed.
- Small EXE / ZIP: requires .NET 10 Desktop Runtime.
- Enter your existing HTTP proxy, choose tools, and apply their settings.
"@ | Set-Content -LiteralPath $notesDestination -Encoding utf8
}

$hashLines = foreach ($releaseFile in @($standaloneExecutable,$lightExecutable,$archivePath)) {
    $fileHash = Get-FileHash -LiteralPath $releaseFile -Algorithm SHA256
    $fileHash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($releaseFile)
}
Set-Content -LiteralPath (Join-Path $releaseDirectory 'SHA256SUMS.txt') -Value $hashLines -Encoding ascii
Write-Output ('Release files ready: ' + $releaseDirectory)
Get-ChildItem -LiteralPath $releaseDirectory -File | Select-Object Name,@{Name='SizeMiB';Expression={[Math]::Round($_.Length / 1MB,2)}}
