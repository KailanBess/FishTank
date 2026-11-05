"""Build a reproducible, allowlisted Debian deployment kit; never include runtime data."""
import argparse
import hashlib
from pathlib import Path
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED

ROOT = Path(__file__).resolve().parent.parent
SERVER_FILES = ('server.py', 'passkeys.py', 'device_status.py', 'passkeys.html', 'passkeys.js',
                'requirements.lock', 'requirements.txt', 'environment.example',
                'environment.selfhosted.example', 'Caddyfile.example', 'gammu-sandbox.conf.example',
                'fishtank-mfa.service', 'install-debian.sh', 'README.md', 'SELF-HOSTED-SETUP.md', 'FISHYSERVER-SETUP.md')
DOCS = ('SERVER-EXPANSION.md', 'APP-EXPANSION.md')


def package(destination):
    files = {f'server/{name}': ROOT / 'server' / name for name in SERVER_FILES}
    files.update({f'docs/{name}': ROOT / 'docs' / name for name in DOCS})
    content = {}
    for name, path in files.items():
        if path.is_symlink() or not path.is_file():
            raise ValueError(f'Missing or linked release source: {name}')
        content[name] = path.read_bytes()
    content['README.txt'] = (b'FishTank Debian server deployment kit\n\n'
        b'Extract the whole archive. Read server/README.md and docs/SERVER-EXPANSION.md.\n'
        b'Only example configuration is included. Supply real secrets locally.\n'
        b'Review server/install-debian.sh before running as root. It installs initial files only; '
        b'it does not start/expose the service or replace an existing deployment.\n'
        b'SMS needs a modem/SIM or configured gateway; mail needs configured delivery.\n')
    content['SHA256SUMS.txt'] = ''.join(f'{hashlib.sha256(data).hexdigest()}  {name}\n' for name, data in sorted(content.items())).encode()
    destination.parent.mkdir(parents=True, exist_ok=True)
    with ZipFile(destination, 'w', compression=ZIP_DEFLATED) as archive:
        for name, data in sorted(content.items()):
            info = ZipInfo(name, (2020, 1, 1, 0, 0, 0))
            info.create_system = 3
            info.external_attr = (0o100755 if name.endswith('.sh') else 0o100644) << 16
            info.compress_type = ZIP_DEFLATED
            archive.writestr(info, data)
    return hashlib.sha256(destination.read_bytes()).hexdigest()


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=ROOT / 'packages/FishTank-server-kit.zip')
    args = parser.parse_args()
    print(f'{package(args.output)}  {args.output.name}')
