$ErrorActionPreference = "Stop"
dotnet restore VirtualSmartMotionCell.sln
dotnet build VirtualSmartMotionCell.sln -c Release --no-restore
dotnet run --project tests/VirtualSmartMotionCell.Specs -c Release --no-build
dotnet run --project tests/VirtualSmartMotionCell.IntegrationSpecs -c Release --no-build
npm ci --prefix web/viewer --no-audit --no-fund
npm run check --prefix web/viewer
npm run build --prefix web/viewer
python scripts/check_repo.py
python -m pytest -q reference/python-simulator/tests
