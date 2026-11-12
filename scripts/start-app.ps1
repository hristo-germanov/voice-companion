param([switch]$NoBrowser)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$apiDir = Join-Path $repoRoot 'apps\api'
$webDir = Join-Path $repoRoot 'apps\web'
$runtimeDir = Join-Path $repoRoot 'artifacts\launcher'
$apiUrl = 'http://127.0.0.1:17832/api/health'
$webUrl = 'http://localhost:4200/'

function Test-Ready([string]$Url) {
    try {
        if ($Url -eq $apiUrl) {
            return (Invoke-RestMethod $Url -TimeoutSec 2).status -eq 'ok'
        }
        return (Invoke-WebRequest $Url -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200
    } catch { return $false }
}

function Assert-FreePort([int]$Port) {
    if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
        throw "Port $Port is occupied but the service is not responding. Check the existing process before trying again."
    }
}

function Wait-Ready([string]$Url, $Process, [string]$LogPrefix) {
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Ready $Url) { return }
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Startup failed. Check $LogPrefix.out.log and $LogPrefix.err.log."
        }
        Start-Sleep -Milliseconds 500
    }
    throw "Startup timed out. Check $LogPrefix.out.log and $LogPrefix.err.log."
}

New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
$logStamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'

if (Test-Ready $apiUrl) {
    Write-Host 'Audio service is already running.'
} else {
    Assert-FreePort 17832
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET SDK and ASP.NET Core 8 runtime first.' }
    $configPath = Join-Path $apiDir 'appsettings.json'
    if (-not (Test-Path -LiteralPath $configPath)) {
        $config = Get-Content (Join-Path $apiDir 'appsettings.example.json') -Raw | ConvertFrom-Json
        $config.Api.ApiKey = [guid]::NewGuid().ToString('N')
        $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding UTF8
        Write-Host 'Created local configuration with a private capture token.'
    }

    $buildDir = Join-Path $runtimeDir 'api'
    $apiExe = Join-Path $buildDir 'ChatGPTVoiceCompanion.Api.exe'
    $sourceFiles = @(Get-ChildItem -LiteralPath $apiDir -File | Where-Object { $_.Extension -in '.cs', '.csproj' })
    foreach ($folder in 'Controllers', 'Data', 'Migrations', 'Models', 'Options', 'Services') {
        $sourceFiles += Get-ChildItem -LiteralPath (Join-Path $apiDir $folder) -Filter '*.cs' -File -Recurse
    }
    $needsBuild = -not (Test-Path -LiteralPath $apiExe)
    if (-not $needsBuild) {
        $builtAt = (Get-Item -LiteralPath $apiExe).LastWriteTimeUtc
        $needsBuild = @($sourceFiles | Where-Object { $_.LastWriteTimeUtc -gt $builtAt }).Count -gt 0
    }
    if ($needsBuild) {
        Write-Host 'Building the audio service...'
        $env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet'
        $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        & dotnet build (Join-Path $apiDir 'ChatGPTVoiceCompanion.Api.csproj') --output $buildDir --disable-build-servers '-p:EnableDefaultContentItems=false' '-p:EnableDefaultNoneItems=false' '-p:StaticWebAssetsEnabled=false'
        if ($LASTEXITCODE -ne 0) { throw 'Audio service build failed. See the output above.' }
    }
    $apiLog = Join-Path $runtimeDir "api-$logStamp"
    $apiProcess = Start-Process -FilePath $apiExe -WorkingDirectory $apiDir -WindowStyle Hidden -PassThru -RedirectStandardOutput "$apiLog.out.log" -RedirectStandardError "$apiLog.err.log"
    Wait-Ready $apiUrl $apiProcess $apiLog
    Write-Host 'Audio service started. The voice model warms up in the background.'
}

if (Test-Ready $webUrl) {
    Write-Host 'Dashboard is already running.'
} else {
    Assert-FreePort 4200
    $node = Get-Command node.exe -ErrorAction SilentlyContinue
    if (-not $node) { throw 'Install Node.js and npm first.' }
    $installedLock = Join-Path $webDir 'node_modules\.package-lock.json'
    $lockFile = Join-Path $webDir 'package-lock.json'
    if (-not (Test-Path -LiteralPath $installedLock) -or (Get-Item -LiteralPath $lockFile).LastWriteTimeUtc -gt (Get-Item -LiteralPath $installedLock).LastWriteTimeUtc) {
        Write-Host 'Installing dashboard dependencies...'
        Push-Location $webDir
        try {
            & npm.cmd ci
            if ($LASTEXITCODE -ne 0) { throw 'Dashboard dependency installation failed.' }
        } finally { Pop-Location }
    }
    $ng = Join-Path $webDir 'node_modules\@angular\cli\bin\ng.js'
    $webLog = Join-Path $runtimeDir "web-$logStamp"
    $webProcess = Start-Process -FilePath $node.Source -ArgumentList @(('"' + $ng + '"'), 'serve', '--host', 'localhost', '--port', '4200') -WorkingDirectory $webDir -WindowStyle Hidden -PassThru -RedirectStandardOutput "$webLog.out.log" -RedirectStandardError "$webLog.err.log"
    Wait-Ready $webUrl $webProcess $webLog
    Write-Host 'Dashboard started.'
}

Write-Host "Ready: $webUrl"
Write-Host "Services keep running after this window closes. Logs: $runtimeDir"
if (-not $NoBrowser) { Start-Process $webUrl }
