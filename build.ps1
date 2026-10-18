param([switch]$Portable, [switch]$Check)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools\cli'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Push-Location $taskRoot
try {
    $taskProject = 'src\Moru\Moru.csproj'
    & $taskDotnet restore $taskProject --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & $taskDotnet build $taskProject -c Release --no-restore -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if ($Check) {
        & $taskDotnet restore 'tests\Moru.Checks\Moru.Checks.csproj' --configfile NuGet.Config
        if ($LASTEXITCODE -ne 0) { throw 'Check restore failed.' }
        & $taskDotnet run --project 'tests\Moru.Checks\Moru.Checks.csproj' -c Release --no-restore -p:UseSharedCompilation=false -- 'artifacts\check-data'
        if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
    }
    if ($Portable) {
        & $taskDotnet restore $taskProject -r win-x64 -p:SelfContained=true --configfile NuGet.Config
        if ($LASTEXITCODE -ne 0) { throw 'Portable restore failed.' }
        & $taskDotnet publish $taskProject -c Release -r win-x64 --self-contained true --no-restore -p:UseSharedCompilation=false -o 'artifacts\Moru-win-x64'
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        Copy-Item -LiteralPath README.md -Destination 'artifacts\Moru-win-x64\README.md'
        Copy-Item -LiteralPath LICENSE,THIRD-PARTY-NOTICES.md,CHANGELOG.md -Destination 'artifacts\Moru-win-x64'
        New-Item -ItemType Directory -Path 'artifacts\Moru-win-x64\docs' -Force | Out-Null
        Copy-Item -Path 'docs\*' -Destination 'artifacts\Moru-win-x64\docs' -Recurse -Force
        New-Item -ItemType Directory -Path 'artifacts\Moru-win-x64\licenses' -Force | Out-Null
        Copy-Item -Path 'licenses\*' -Destination 'artifacts\Moru-win-x64\licenses' -Recurse -Force
        Compress-Archive -Path 'artifacts\Moru-win-x64' -DestinationPath 'artifacts\Moru-win-x64.zip' -Force
    }
} finally { Pop-Location }
