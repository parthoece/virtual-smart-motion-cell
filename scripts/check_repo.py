#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

try:
    import yaml
except ImportError:
    yaml = None

root = Path(__file__).resolve().parents[1]
errors: list[str] = []
notes: list[str] = []
ignored_parts = {'.git', 'bin', 'obj', 'node_modules', '.pytest_cache', '__pycache__', 'artifacts', 'runtime-data', 'runs', 'build', 'dist'}


def ignored(path: Path) -> bool:
    parts = path.relative_to(root).parts
    return any(part in ignored_parts or part.endswith('.egg-info') for part in parts)


def require(relative: str) -> Path:
    path = root / relative
    if not path.exists():
        errors.append(f'missing {relative}')
    return path


required = [
    'README.md', 'LICENSE', 'CONTRIBUTING.md', 'CODE_OF_CONDUCT.md', 'SECURITY.md',
    'GOVERNANCE.md', 'ROADMAP.md', 'ARCHITECTURE.md', 'VirtualSmartMotionCell.sln',
    'CODEOWNERS', 'PLATFORM_SUPPORT.md', 'COMPATIBILITY.md', 'VERSION',
    'docs/portfolio-evidence-matrix.md', 'docs/opc-ua.md', 'docs/mes-simulator.md',
    'docs/observability.md', 'docs/manufacturing-data.md', 'docs/cross-platform-release.md',
    'research/pyproject.toml', 'research/vsmc_research/environment.py',
    'research/vsmc_research/runner.py', 'research/vsmc_research/dataset.py',
    'research/studio/index.html', 'benchmarks/manifests/machine-fault.yaml',
    'benchmarks/schemas/experiment-manifest.schema.json',
    'docs/research/final-research-plan.md', 'docs/research/research-questions.md',
]
for relative in required:
    require(relative)

# Structured files.
for path in root.rglob('*.json'):
    if ignored(path):
        continue
    try:
        json.loads(path.read_text(encoding='utf-8'))
    except Exception as exc:
        errors.append(f'invalid JSON {path.relative_to(root)}: {exc}')

if yaml is not None:
    for pattern in ('*.yml', '*.yaml'):
        for path in root.rglob(pattern):
            if ignored(path):
                continue
            try:
                yaml.safe_load(path.read_text(encoding='utf-8'))
            except Exception as exc:
                errors.append(f'invalid YAML {path.relative_to(root)}: {exc}')
else:
    notes.append('PyYAML is not installed; YAML syntax validation skipped')

for pattern in ('*.xml', '*.xaml', '*.axaml', '*.csproj', '*.props'):
    for path in root.rglob(pattern):
        if ignored(path):
            continue
        try:
            ET.parse(path)
        except Exception as exc:
            errors.append(f'invalid XML/XAML {path.relative_to(root)}: {exc}')

# Relative Markdown links, excluding fenced examples.
link_pattern = re.compile(r'\[[^\]]+\]\((?!https?://|mailto:|#)([^)]+)\)')
fence_pattern = re.compile(r'```.*?```', re.DOTALL)
for path in root.rglob('*.md'):
    if ignored(path) or 'reference/python-simulator' in path.as_posix():
        continue
    text = fence_pattern.sub('', path.read_text(encoding='utf-8', errors='replace'))
    for target in link_pattern.findall(text):
        target = target.split('#', 1)[0].strip()
        if not target or target.startswith('YOUR_'):
            continue
        if not (path.parent / target).resolve().exists():
            errors.append(f'broken link {path.relative_to(root)} -> {target}')

# Solution membership and project-reference integrity.
solution_path = root / 'VirtualSmartMotionCell.sln'
solution_text = solution_path.read_text(encoding='utf-8', errors='replace') if solution_path.exists() else ''
solution_projects = {
    match.replace('\\', '/').lower()
    for match in re.findall(r'"([^"\r\n]+\.csproj)"', solution_text, flags=re.IGNORECASE)
}
csprojects = [path for path in root.rglob('*.csproj') if not ignored(path)]
for project in csprojects:
    relative = project.relative_to(root).as_posix().lower()
    if relative not in solution_projects:
        errors.append(f'solution does not include {project.relative_to(root)}')
    try:
        tree = ET.parse(project)
        for reference in tree.findall('.//ProjectReference'):
            include = reference.attrib.get('Include', '')
            if include and not (project.parent / include).resolve().exists():
                errors.append(f'broken ProjectReference {project.relative_to(root)} -> {include}')
    except ET.ParseError:
        pass

# Avalonia bindings must resolve to a public ViewModel property.
xaml = root / 'src/VirtualSmartMotionCell.Hmi/MainWindow.axaml'
view_model = root / 'src/VirtualSmartMotionCell.Hmi/MainWindowViewModel.cs'
if xaml.exists() and view_model.exists():
    bindings = set(re.findall(r'\{Binding\s+([A-Za-z_][A-Za-z0-9_]*)', xaml.read_text(encoding='utf-8')))
    properties = set(re.findall(
        r'public\s+(?:[A-Za-z0-9_?.<>\[\],]+\s+)+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\{|=>)',
        view_model.read_text(encoding='utf-8'),
    ))
    for binding in sorted(bindings - properties):
        errors.append(f'Avalonia binding has no public ViewModel property: {binding}')

# Evidence contracts for the advertised portfolio matrix.
evidence = {
    'cross-platform runtime': [
        ('global.json', '10.0.301'),
        ('.github/workflows/ci.yml', 'windows-latest'),
        ('.github/workflows/ci.yml', 'macos-latest'),
        ('.github/workflows/ci.yml', 'ubuntu-latest'),
    ],
    'equipment architecture': [
        ('src/VirtualSmartMotionCell.Domain/VirtualSmartMotionCell.Domain.csproj', '<Project'),
        ('src/VirtualSmartMotionCell.Application/VirtualSmartMotionCell.Application.csproj', '<Project'),
        ('src/VirtualSmartMotionCell.AdapterSdk/AdapterContracts.cs', 'IMotionSystem'),
        ('src/VirtualSmartMotionCell.Infrastructure/VirtualSmartMotionCell.Infrastructure.csproj', '<Project'),
    ],
    'machine operation': [
        ('src/VirtualSmartMotionCell.Contracts/Enums.cs', 'Maintenance'),
        ('src/VirtualSmartMotionCell.Contracts/Enums.cs', 'Recovery'),
        ('src/VirtualSmartMotionCell.Application/MachineCoordinator.cs', '"jog"'),
        ('src/VirtualSmartMotionCell.Application/MachineCoordinator.cs', '"start"'),
    ],
    'command processing': [
        ('src/VirtualSmartMotionCell.Application/MachineCommandBus.cs', 'Channel.CreateBounded'),
        ('src/VirtualSmartMotionCell.Contracts/Commands.cs', 'ReasonCode'),
    ],
    'motion abstraction': [
        ('src/VirtualSmartMotionCell.Control/SimulatedMotionSystem.cs', 'SimulatedMotionSystem'),
        ('src/VirtualSmartMotionCell.Control/ReplayMotionSystem.cs', 'ReplayMotionSystem'),
        ('src/VirtualSmartMotionCell.Control/FaultInjectingMotionSystem.cs', 'FaultInjectingMotionSystem'),
    ],
    'hmi': [
        ('src/VirtualSmartMotionCell.Hmi/VirtualSmartMotionCell.Hmi.csproj', 'Avalonia'),
        ('src/VirtualSmartMotionCell.Hmi/MainWindow.axaml', 'Diagnostics'),
        ('src/VirtualSmartMotionCell.Hmi/MainWindow.axaml', 'Maintenance'),
    ],
    'digital twin': [
        ('web/viewer/package.json', 'three'),
        ('web/viewer/app.js', '/ws/state'),
        ('web/viewer/dist/index.html', '<html'),
    ],
    'reliability': [
        ('src/VirtualSmartMotionCell.Application/MachineCoordinator.cs', 'RecoveryRequired'),
        ('tools/VirtualSmartMotionCell.Reliability/Program.cs', '--duration-minutes'),
        ('.github/workflows/reliability.yml', '10000'),
    ],
    'manufacturing data': [
        ('src/VirtualSmartMotionCell.Domain/ManufacturingModels.cs', 'ProductionOrder'),
        ('src/VirtualSmartMotionCell.Infrastructure/FileStores.cs', 'FileProductionRepository'),
        ('src/VirtualSmartMotionCell.Contracts/Snapshots.cs', 'Oee'),
    ],
    'integration': [
        ('src/VirtualSmartMotionCell.OpcUa/MachineNodeManager.cs', 'CustomNodeManager2'),
        ('tools/VirtualSmartMotionCell.MesSimulator/Program.cs', '/api/v1/results'),
        ('src/VirtualSmartMotionCell.Infrastructure/FileStores.cs', 'Idempotency-Key'),
    ],
    'observability': [
        ('src/VirtualSmartMotionCell.Application/MachineTelemetry.cs', 'ActivitySource'),
        ('src/VirtualSmartMotionCell.Api/Program.cs', 'OpenTelemetry'),
        ('src/VirtualSmartMotionCell.Contracts/Commands.cs', 'CorrelationId'),
    ],
    'deployment': [
        ('.github/workflows/release.yml', 'win-x64'),
        ('.github/workflows/release.yml', 'linux-arm64'),
        ('.github/workflows/release.yml', 'osx-arm64'),
    ],
    'testing': [
        ('tests/VirtualSmartMotionCell.Specs/Program.cs', 'fault'),
        ('tests/VirtualSmartMotionCell.IntegrationSpecs/Program.cs', 'Recovery'),
        ('.github/workflows/ci.yml', 'IntegrationSpecs'),
        ('.github/workflows/reliability.yml', 'duration_minutes'),
    ],
}
for area, checks in evidence.items():
    for relative, marker in checks:
        path = root / relative
        if not path.exists():
            errors.append(f'{area}: missing evidence file {relative}')
            continue
        if marker.lower() not in path.read_text(encoding='utf-8', errors='ignore').lower():
            errors.append(f'{area}: {relative} does not contain expected marker {marker!r}')


# Research benchmark contracts.
research_evidence = {
    'dynamic environment': [
        ('research/vsmc_research/environment.py', 'DynamicGantryEnvironment'),
        ('benchmarks/manifests/machine-fault.yaml', 'increased_friction'),
        ('docs/research/dynamic-benchmark.md', 'hybrid'),
    ],
    'ground-truth separation': [
        ('research/vsmc_research/runner.py', 'scenario_intervals'),
        ('research/vsmc_research/dataset.py', 'target_operational_condition'),
        ('docs/research/ground-truth-and-labeling.md', 'Observable data'),
    ],
    'multimodal data': [
        ('research/vsmc_research/network.py', 'MinimalPcapNgWriter'),
        ('research/vsmc_research/recorder.py', 'to_parquet'),
        ('research/vsmc_research/dataset.py', 'aggregate_flows'),
    ],
    'ethercat protocol': [
        ('research/vsmc_research/ethercat.py', 'ETHERCAT_ETHERTYPE = 0x88A4'),
        ('research/vsmc_research/network.py', 'EtherCATNetwork'),
        ('docs/research/ethercat-protocol.md', 'LRW'),
        ('research/tests/test_ethercat_protocol.py', 'working_counter'),
    ],
    'visual workflow': [
        ('research/studio/index.html', 'Visual Experiment Studio'),
        ('research/vsmc_research/api.py', '/api/experiments'),
        ('docs/research/visual-experiment-studio.md', 'same manifest'),
    ],
    'reproducibility': [
        ('research/vsmc_research/replay.py', 'transition_match'),
        ('research/vsmc_research/recorder.py', 'checksums.sha256'),
        ('docs/research/publication-and-reproducibility.md', 'one-command reproduction'),
    ],
    'research planning': [
        ('docs/research/final-research-plan.md', 'Phase R1'),
        ('docs/research/research-questions.md', 'RQ1.'),
        ('docs/research/practicality-and-community-value.md', 'Academic value'),
    ],
}
for area, checks in research_evidence.items():
    for relative, marker in checks:
        path = root / relative
        if not path.exists():
            errors.append(f'research {area}: missing evidence file {relative}')
            continue
        if marker.lower() not in path.read_text(encoding='utf-8', errors='ignore').lower():
            errors.append(f'research {area}: {relative} does not contain expected marker {marker!r}')

manifest_paths = sorted((root / 'benchmarks/manifests').glob('*.yaml'))
if len(manifest_paths) < 4:
    errors.append('research benchmark must include normal, machine-fault, network-fault, and combined manifests')
for manifest in manifest_paths:
    packaged = root / 'research/vsmc_research/templates' / manifest.name
    if not packaged.exists() or packaged.read_bytes() != manifest.read_bytes():
        errors.append(f'packaged research template is missing or stale: {manifest.name}')
for asset in ('index.html', 'styles.css', 'app.js'):
    source = root / 'research/studio' / asset
    packaged = root / 'research/vsmc_research/studio' / asset
    if not packaged.exists() or packaged.read_bytes() != source.read_bytes():
        errors.append(f'packaged visual studio asset is missing or stale: {asset}')

# Viewer must be reproducible and bundled, with no runtime CDN dependency.
for relative in ('web/viewer/package-lock.json', 'web/viewer/dist/assets'):
    require(relative)
viewer_source = (root / 'web/viewer/app.js').read_text(encoding='utf-8', errors='ignore') if (root / 'web/viewer/app.js').exists() else ''
if "from 'three'" not in viewer_source or 'OrbitControls' not in viewer_source:
    errors.append('viewer must import Three.js and OrbitControls from the locked npm dependency')
for path in (root / 'web/viewer/dist').rglob('*') if (root / 'web/viewer/dist').exists() else []:
    if path.is_file() and 'cdn.jsdelivr.net' in path.read_text(encoding='utf-8', errors='ignore'):
        errors.append(f'bundled viewer depends on a runtime CDN: {path.relative_to(root)}')

# Known regression and publication placeholders.
coordinator = root / 'src/VirtualSmartMotionCell.Application/MachineCoordinator.cs'
if coordinator.exists() and 'Snapshot(motionState)' in coordinator.read_text(encoding='utf-8'):
    errors.append('known compile regression found: undefined motionState')
for placeholder in ('YOUR_GITHUB_HANDLE', 'YOUR_NAME', 'SECURITY_CONTACT@example.com'):
    count = sum(
        path.read_text(encoding='utf-8', errors='ignore').count(placeholder)
        for path in root.rglob('*')
        if path.is_file() and not ignored(path) and path.stat().st_size < 2_000_000
    )
    if not count:
        errors.append(f'expected publishing placeholder not found: {placeholder}')

if errors:
    print('\n'.join('ERROR ' + error for error in errors))
    if notes:
        print('\n'.join('NOTE ' + note for note in notes))
    sys.exit(1)

print(f'repository contracts valid: {len(csprojects)} .NET projects, {len(evidence)} portfolio areas, {len(research_evidence)} research areas, structured files, links, XAML bindings, and release evidence')
if notes:
    print('\n'.join('NOTE ' + note for note in notes))
