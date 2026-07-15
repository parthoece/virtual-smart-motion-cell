.PHONY: restore build test integration viewer run mes hmi check reliability container
restore:
	dotnet restore VirtualSmartMotionCell.sln
build:
	dotnet build VirtualSmartMotionCell.sln -c Release
test:
	dotnet run --project tests/VirtualSmartMotionCell.Specs -c Release
integration:
	dotnet run --project tests/VirtualSmartMotionCell.IntegrationSpecs -c Release
viewer:
	npm ci --prefix web/viewer && npm run build --prefix web/viewer
run:
	dotnet run --project src/VirtualSmartMotionCell.Api
mes:
	dotnet run --project tools/VirtualSmartMotionCell.MesSimulator
hmi:
	dotnet run --project src/VirtualSmartMotionCell.Hmi
check:
	./scripts/dev-check.sh
reliability:
	dotnet run --project tools/VirtualSmartMotionCell.Reliability -c Release -- --cycles 10000
container:
	docker compose up --build

.PHONY: research-install research-test research-run research-studio
research-install:
	python -m pip install -e "research[dev]"
research-test:
	pytest -q research/tests
research-run:
	vsmc-bench run benchmarks/manifests/machine-fault.yaml --output runs
research-studio:
	vsmc-studio --host 127.0.0.1 --port 8090
research-validate:
	python scripts/validate_research_bundle.py "$${BUNDLE:?set BUNDLE=/path/to/experiment}"
research-reproduce:
	./scripts/reproduce-research-foundation.sh
