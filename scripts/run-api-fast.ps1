$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$apiDir = Join-Path $repoRoot "apps\api"

Push-Location $apiDir
try {
  $env:DOTNET_CLI_HOME = Join-Path $repoRoot ".dotnet"
  $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
  $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

  dotnet run --no-restore --no-build
}
finally {
  Pop-Location
}
