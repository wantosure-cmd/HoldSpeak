param([string]$Runtime = 'win-x64', [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$repoPath = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $repoPath '.tools/dotnet/dotnet.exe'
$sdk = if (Test-Path $localSdk) { $localSdk } else { 'dotnet' }
if ($Runtime -notin @('win-x64', 'win-arm64')) { throw 'Unsupported Windows runtime' }
$outputPath = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoPath "artifacts/HoldSpeak-$Runtime" }
& $sdk publish (Join-Path $repoPath 'src/HoldSpeak.App/HoldSpeak.App.csproj') -c Release -r $Runtime --self-contained true -p:HoldSpeakPlatform=Windows --disable-build-servers -m:1 -o $outputPath
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Copy-Item -LiteralPath (Join-Path $repoPath 'scripts/Launch-HoldSpeak.cmd') -Destination $outputPath
Copy-Item -LiteralPath (Join-Path $repoPath 'README.md'),(Join-Path $repoPath 'LICENSE'),(Join-Path $repoPath 'THIRD-PARTY-NOTICES.md') -Destination $outputPath
Copy-Item -LiteralPath (Join-Path $repoPath 'ATTRIBUTION.md') -Destination $outputPath
Copy-Item -LiteralPath (Join-Path $repoPath 'profiles') -Destination $outputPath -Recurse -Force
Write-Output $outputPath
