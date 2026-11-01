#!/usr/bin/env python3
"""Choose a hosted runner filesystem for the Framework qualification VM."""

import argparse
from pathlib import Path
import shutil
import subprocess
import sys


REQUIRED_FREE_BYTES = 60 * 1024**3


def choose_storage(workspace, temporary):
    """Prefer the checkout, otherwise a sufficiently large mounted temp disk.

    Returning the checkout as a fallback deliberately leaves the launcher's
    independent 60 GiB preflight in charge of reporting an unsuitable host.
    """
    if shutil.disk_usage(workspace).free >= REQUIRED_FREE_BYTES:
        return 'workspace'
    if temporary.is_symlink() or not temporary.is_mount():
        return 'workspace'
    filesystem = subprocess.run(
        ['findmnt', '--noheadings', '--output', 'FSTYPE', '--target', str(temporary)],
        check=True, capture_output=True, text=True, timeout=10).stdout.strip()
    if filesystem in ('tmpfs', 'ramfs', 'devtmpfs') or not filesystem:
        return 'workspace'
    if shutil.disk_usage(temporary).free >= REQUIRED_FREE_BYTES:
        return 'temporary'
    return 'workspace'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workspace', type=Path, required=True)
    parser.add_argument('--temporary', type=Path, default=Path('/mnt'))
    args = parser.parse_args()
    try:
        print(choose_storage(args.workspace, args.temporary))
    except (OSError, subprocess.SubprocessError) as error:
        # Do not turn a missing/unsupported alternate mount into a skipped VM
        # report: the launcher still evaluates the regular workspace itself.
        print(f'Could not inspect alternate VM storage: {error}', file=sys.stderr)
        print('workspace')


if __name__ == '__main__':
    main()
