#!/usr/bin/env python3
"""Enable website providers using existing server credentials; never print their values."""
import argparse
from datetime import datetime, timezone
import os
from pathlib import Path
import re
import tempfile


def enable(path: Path) -> bool:
    if path.is_symlink() or not path.is_file():
        raise ValueError('Expected a regular backend environment file.')
    original = path.read_text(encoding='utf-8')
    values = dict(line.split('=', 1) for line in original.splitlines() if '=' in line and not line.lstrip().startswith('#'))

    def configured(prefix, *names):
        return next((values[prefix + name].strip() for name in names
                     if values.get(prefix + name, '').strip()
                     and not values[prefix + name].strip().startswith('YOUR_')), None)

    for provider in ('Google', 'Facebook'):
        prefix = 'ExternalAuth__' + provider + '__'
        identifier = configured(prefix, 'AppId', 'ClientId')
        secret = configured(prefix, 'AppSecret', 'ClientSecret')
        if not identifier or not secret:
            raise ValueError(provider + ' credentials are missing; configuration was not changed.')
        pattern = r'[A-Za-z0-9._-]+\.apps\.googleusercontent\.com' if provider == 'Google' else r'[0-9]+'
        if not re.fullmatch(pattern, identifier):
            raise ValueError(provider + ' client ID has an invalid format; configuration was not changed.')
    keys = ('ExternalAuth__Google__Enabled', 'ExternalAuth__Facebook__Enabled')
    if all(values.get(key, '').strip().lower() == 'true' for key in keys):
        return False
    updated = '\n'.join(line for line in original.splitlines() if line.split('=', 1)[0] not in keys)
    updated = updated.rstrip('\n') + '\n' + ''.join(key + '=true\n' for key in keys)
    info = path.stat()
    backup = path.with_name(path.name + '.before-external-auth-' + datetime.now(timezone.utc).strftime('%Y%m%d%H%M%S%f'))
    descriptor = os.open(backup, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, 'w', encoding='utf-8', newline='') as output:
        output.write(original)
        output.flush()
        os.fsync(output.fileno())
    descriptor, staged = tempfile.mkstemp(prefix='.treba-external-auth-', dir=path.parent)
    try:
        with os.fdopen(descriptor, 'w', encoding='utf-8', newline='') as output:
            output.write(updated)
            output.flush()
            os.fsync(output.fileno())
        if hasattr(os, 'chown'):
            os.chown(backup, info.st_uid, info.st_gid)
            os.chown(staged, info.st_uid, info.st_gid)
        os.chmod(staged, 0o600)
        os.replace(staged, path)
    finally:
        if os.path.exists(staged):
            os.unlink(staged)
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--env-file', type=Path, default=Path('/home/ubuntu/treba-backend.env'))
    arguments = parser.parse_args()
    try:
        changed = enable(arguments.env_file)
    except (OSError, ValueError) as error:
        # Exceptions here contain operation context, never credential values.
        raise SystemExit(str(error)) from None
    print('Google and Facebook enabled; previous configuration backed up securely.' if changed else 'Google and Facebook are already enabled.')


if __name__ == '__main__':
    main()
