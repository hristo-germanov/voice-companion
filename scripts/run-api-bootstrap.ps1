$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$apiDir = Join-Path $repoRoot "apps\api"
$assetsFile = Join-Path $apiDir "obj\project.assets.json"
$builtDll = Join-Path $apiDir "bin\Debug\net8.0\ChatGPTVoiceCompanion.Api.dll"

Push-Location $apiDir
try {
  $env:DOTNET_CLI_HOME = Join-Path $repoRoot ".dotnet"
  $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
  $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

  if (-not (Test-Path $assetsFile)) {
    Write-Host "No restore assets found. Running restore..."
    dotnet restore
  } else {
    Write-Host "Restore assets found. Skipping restore."
  }

  if (-not (Test-Path $builtDll)) {
    Write-Host "No build output found. Running build..."
    dotnet build --no-restore
  } else {
    Write-Host "Build output found. Skipping build."
  }

  Write-Host "Starting API (fast mode)..."
  dotnet run --no-restore --no-build
}
finally {
  Pop-Location
}
