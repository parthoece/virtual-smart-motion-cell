$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $Root

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker is required. Install Docker Desktop or Docker Engine with Compose v2."
}
docker compose version | Out-Null
New-Item -ItemType Directory -Force -Path "runs" | Out-Null

docker compose up --build -d mes-simulator machine-runtime research-studio
if ($LASTEXITCODE -ne 0) { throw "Docker Compose startup failed." }

docker compose --profile bootstrap run --rm complete-bootstrap
if ($LASTEXITCODE -ne 0) { throw "Stack bootstrap or reference benchmark failed." }

$Latest = ""
if (Test-Path "runs/LATEST_BUNDLE") {
    $Latest = (Get-Content "runs/LATEST_BUNDLE" -Raw).Trim()
}

Write-Host ""
Write-Host "Virtual Smart Motion Cell is running."
Write-Host ""
$RuntimePort = if ($env:VSMC_RUNTIME_PORT) { $env:VSMC_RUNTIME_PORT } else { "8080" }
$MesPort = if ($env:VSMC_MES_PORT) { $env:VSMC_MES_PORT } else { "8090" }
$StudioPort = if ($env:VSMC_STUDIO_PORT) { $env:VSMC_STUDIO_PORT } else { "8091" }
$OpcUaPort = if ($env:VSMC_OPCUA_PORT) { $env:VSMC_OPCUA_PORT } else { "4840" }
Write-Host "Machine viewer/API:  http://localhost:$RuntimePort"
Write-Host "MES simulator:       http://localhost:$MesPort"
Write-Host "Experiment Studio:   http://localhost:$StudioPort"
Write-Host "OPC UA endpoint:     opc.tcp://localhost:$OpcUaPort/vsmc"
if ($Latest) {
    Write-Host "Research report:     $Root/runs/$Latest/report/index.html"
}
Write-Host ""
Write-Host "Stop the stack with: docker compose down"
