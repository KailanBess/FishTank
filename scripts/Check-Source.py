"""Scan tracked source for common credential formats and emit a dependency SBOM."""
import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path
from urllib.parse import quote
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PATTERNS = [re.compile(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'),
            re.compile(r'gh[pousr]_[A-Za-z0-9]{36}\b'), re.compile(r'github_pat_[A-Za-z0-9_]{60,}'),
            re.compile(r'\bsk-(?:proj-)?[A-Za-z0-9_-]{40,}')]


def check(output):
    names = subprocess.check_output(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'], cwd=ROOT).decode().split('\0')
    failures, components = [], []
    for name in sorted(set(names)):
        file = ROOT / name
        if not name or not file.is_file() or file.stat().st_size > 2 * 1024 * 1024 or file.suffix.lower() in ('.exe', '.dll', '.zip', '.png', '.ico'):
            continue
        try:
            text = file.read_text(encoding='utf-8-sig')
        except UnicodeError:
            continue
        for number, line in enumerate(text.splitlines(), 1):
            if any(pattern.search(line) for pattern in PATTERNS):
                failures.append(f'{name}:{number}: possible credential material (value withheld)')
    lock = ROOT / 'server/requirements.lock'
    for line in lock.read_text().splitlines():
        if not line or line.startswith('#'):
            continue
        name, version = line.split('==')
        canonical = re.sub('[-_.]+', '-', name).lower()
        components.append({'type': 'library', 'name': name, 'version': version, 'purl': f'pkg:pypi/{canonical}@{version}',
                           'properties': [{'name': 'fishtank:dependency-source', 'value': 'server/requirements.lock'}]})
    for project in sorted((ROOT / 'desktop').glob('*.csproj')):
        for package in ET.parse(project).iter('PackageReference'):
            name, version = package.attrib['Include'], package.attrib['Version']
            components.append({'type': 'library', 'name': name, 'version': version, 'purl': f'pkg:nuget/{quote(name)}@{version}',
                               'properties': [{'name': 'fishtank:dependency-source', 'value': str(project.relative_to(ROOT)).replace('\\', '/')} ]})
    document = {'bomFormat': 'CycloneDX', 'specVersion': '1.5', 'version': 1,
                'metadata': {'component': {'type': 'application', 'name': 'FishTank'},
                             'properties': [{'name': 'fishtank:scope', 'value': 'Pinned server dependencies and direct desktop package references; operating system and Python/.NET runtimes are supplied separately.'}]},
                'components': components}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(document, indent=2) + '\n', encoding='utf-8')
    if failures:
        raise SystemExit('\n'.join(failures))
    print(f'Source credential scan passed; SBOM records {len(components)} dependencies at {output.name}.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--sbom', type=Path, default=ROOT / 'packages/FishTank-sbom.json')
    check(parser.parse_args().sbom)
