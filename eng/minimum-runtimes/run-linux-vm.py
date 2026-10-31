#!/usr/bin/env python3
"""Run the prepared Windows qualification VM on an isolated Linux x64 host."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import signal
import subprocess
import time
import uuid
import xml.etree.ElementTree as ET
import zipfile

IMAGE = 'dockurr/windows:6.06@sha256:16fd7c39a3e48bb946d29c59dc6015942e5b900a1fa5d7e5552053bfc48ee2ea'


def command(*args, check=True, timeout=120):
    return subprocess.run(args, check=check, capture_output=True, text=True, timeout=timeout)


def sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def validate_setup(setup, commit, asset):
    manifest = read_json(setup / 'bundle.json')
    if (manifest['sourceCommit'] != commit or manifest['testAsset'] != asset
            or manifest['jitMode'] != 'legacy' or manifest['requiredCases'] < 829):
        raise ValueError('Bundle revision, asset, JIT mode or complete-suite count mismatch.')
    if sha256(setup / 'tests.zip') != manifest['testsSha256']:
        raise ValueError('Test ZIP does not match its manifest.')
    with zipfile.ZipFile(setup / 'tests.zip') as archive:
        for name, digest in manifest['binaries'].items():
            if Path(name).name != name or hashlib.sha256(archive.read(name)).hexdigest() != digest:
                raise ValueError(f'Bundled binary hash mismatch: {name}')
        if int(archive.read('required-tests.txt')) != manifest['requiredCases']:
            raise ValueError('Bundled required-case count differs from the manifest.')
    return manifest


def validate_results(results, manifest):
    status = read_json(results / 'status.json')
    evidence = read_json(results / 'environment.json')
    if (status.get('passed') is not True or status.get('exactFramework472') is not True
            or status.get('frameworkRelease') not in (461808, 461814)
            or status.get('sourceCommit') != manifest['sourceCommit']
            or status.get('testAsset') != manifest['testAsset']
            or status.get('testsSha256') != manifest['testsSha256']):
        raise ValueError('The guest did not qualify this exact bundle on actual Framework 4.7.2.')
    if (evidence.get('exactFramework472') is not True
            or evidence.get('frameworkRelease') != status['frameworkRelease']
            or evidence.get('testProcessArchitecture') != 'x64'
            or evidence.get('requestedJitMode') != 'legacy'
            or evidence.get('runtimeOverrides')
            or evidence.get('childRuntimeOverrides') != {'COMPlus_UseLegacyJit': '1'}
            or not any(module.get('name', '').lower() == 'compatjit.dll'
                       for module in evidence.get('loadedJitModules', []))
            or not re.fullmatch('[0-9a-f]{64}', evidence.get('testConfigurationSha256') or '')):
        raise ValueError('Missing actual x64 legacy-JIT/configuration evidence.')
    for field, name in [('librarySha256', 'wordnet.dll'), ('testSha256', 'wordnet.Conversion.Tests.exe')]:
        if evidence.get(field) != manifest['binaries'][name]:
            raise ValueError(f'The executed {name} does not match the bundle.')
    tree = ET.parse(results / 'net472.trx')
    counters = next(node for node in tree.iter() if node.tag.rsplit('}', 1)[-1] == 'Counters')
    required = manifest['requiredCases']
    total = int(counters.get('total', 0))
    if (total < required or int(counters.get('passed', 0)) != total
            or ('executed' in counters.attrib and int(counters.get('executed', 0)) != total)
            or any(int(counters.get(field, 0)) for field in ['failed', 'error', 'timeout', 'aborted', 'inconclusive',
                'passedButRunAborted', 'notRunnable', 'notExecuted', 'disconnected', 'inProgress', 'pending'])):
        raise ValueError('The complete Framework suite did not pass without skips/failures.')
    return {'passed': True, 'frameworkRelease': status['frameworkRelease'],
            'cases': total, 'commit': manifest['sourceCommit'], 'asset': manifest['testAsset'],
            'librarySha256': evidence['librarySha256'], 'testSha256': evidence['testSha256']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--setup', required=True, type=Path)
    parser.add_argument('--work', required=True, type=Path)
    parser.add_argument('--results', required=True, type=Path)
    parser.add_argument('--commit', required=True)
    parser.add_argument('--asset', choices=['source', 'package'], required=True)
    parser.add_argument('--timeout-seconds', type=int, default=3600)
    args = parser.parse_args()
    if not re.fullmatch('[0-9a-f]{40}', args.commit) or not 60 <= args.timeout_seconds <= 5400:
        parser.error('Use a full commit SHA and a bounded 60–5400 second VM deadline.')
    setup, work, results = (path.resolve() for path in (args.setup, args.work, args.results))
    if (work == results or work in results.parents or results in work.parents
            or work == setup or work in setup.parents or setup in work.parents):
        parser.error('Setup, fresh VM state and fresh evidence must be separate directories.')
    if work.exists() or results.exists():
        parser.error('Refusing to overwrite existing VM state or evidence.')
    results.mkdir(parents=True)
    project = 'wordnet-472-' + uuid.uuid4().hex[:12]
    password = uuid.uuid4().hex + 'aA1!'
    compose = ['docker', 'compose', '--project-name', project,
               '--env-file', str(work / 'secrets.env'), '-f', str(work / 'compose.json')]
    owned_work = False
    compose_written = False
    pulled_image = False
    image_preexisting = True
    failure = None
    cleanup = {'project': project, 'containersRemoved': False, 'networkRemoved': False,
               'stateRemoved': False, 'imageRemoved': False}

    def interrupt(_number, _frame):
        raise KeyboardInterrupt('VM run interrupted; cleaning up owned resources.')

    signal.signal(signal.SIGTERM, interrupt)
    try:
        manifest = validate_setup(setup, args.commit, args.asset)
        shutil.copy2(setup / 'bundle.json', results / 'bundle.json')
        if platform.system() != 'Linux' or platform.machine() not in ('x86_64', 'amd64'):
            raise RuntimeError('Use a native Linux x64 Docker/KVM host.')
        engine = json.loads(command('docker', 'info', '--format', '{{json .}}').stdout)
        if engine['OSType'] != 'linux' or engine['Architecture'] not in ('x86_64', 'amd64'):
            raise RuntimeError('Docker must run Linux x64 containers.')
        if engine['MemTotal'] < 6 * 1024**3:
            raise RuntimeError('The Windows VM requires at least 6 GiB of Docker memory.')
        work.parent.mkdir(parents=True, exist_ok=True)
        if shutil.disk_usage(work.parent).free < 60 * 1024**3:
            raise RuntimeError('The VM requires at least 60 GiB free for its disk, ISO and setup.')
        if not os.access('/dev/kvm', os.R_OK | os.W_OK):
            raise RuntimeError('Usable hardware virtualization (/dev/kvm) is required on this CI host.')
        command('docker', 'compose', 'version')
        work.mkdir(mode=0o700)
        owned_work = True
        for folder in ['storage', 'shared', 'oem']:
            (work / folder).mkdir()
        for name in ['install.bat', 'qualify.ps1', 'tests.zip', 'bundle.json']:
            shutil.copy2(setup / name, work / 'oem' / name)
        secret = work / 'secrets.env'
        secret.write_text(f'WORDNET_VM_PASSWORD={password}\n')
        secret.chmod(0o600)
        service = {
            'image': IMAGE, 'platform': 'linux/amd64',
            'environment': {'VERSION': '2019', 'VERIFY': 'Y', 'RAM_SIZE': '4G', 'CPU_CORES': '2',
                            'DISK_SIZE': '40G', 'DISK_FMT': 'qcow2', 'NETWORK': 'user', 'KVM': 'Y',
                            'USERNAME': 'Wordnet', 'PASSWORD': '${WORDNET_VM_PASSWORD}', 'LOG': 'Y'},
            'devices': ['/dev/kvm:/dev/kvm'],
            'volumes': ['./storage:/storage', './oem:/oem:ro', './shared:/shared'],
            'restart': 'no', 'stop_grace_period': '30s'}
        (work / 'compose.json').write_text(json.dumps({'services': {'windows': service}}, indent=2))
        compose_written = True
        image_preexisting = command('docker', 'image', 'inspect', IMAGE, check=False).returncode == 0
        command('docker', 'pull', '--platform', 'linux/amd64', IMAGE, timeout=600)
        pulled_image = True
        command('docker', 'run', '--rm', '--network', 'none', '--device', '/dev/kvm',
                '--entrypoint', '/bin/sh', IMAGE, '-c', 'test -r /dev/kvm && test -w /dev/kvm')
        command(*compose, 'config', '--quiet')
        command(*compose, 'up', '-d')
        print(f'Windows 2019 VM started for {args.asset}; bounded deadline {args.timeout_seconds}s.', flush=True)
        deadline = time.monotonic() + args.timeout_seconds
        exported = work / 'shared/results'
        while time.monotonic() < deadline:
            if (exported / 'export-complete.json').exists():
                marker = read_json(exported / 'export-complete.json')
                for path in exported.iterdir():
                    if path.is_file():
                        shutil.copy2(path, results / path.name)
                if marker['exitCode'] != 0:
                    raise RuntimeError('Guest setup/tests failed; see preserved status.json and setup.log.')
                verification = validate_results(results, manifest)
                (results / 'verification.json').write_text(json.dumps(verification, indent=2) + '\n')
                print(json.dumps(verification), flush=True)
                break
            state = command(*compose, 'ps', '--all', '--format', 'json').stdout
            if not state.strip() or re.search(r'"State"\s*:\s*"(exited|dead)"', state):
                raise RuntimeError('Windows VM exited before exporting complete qualification evidence.')
            time.sleep(10)
        else:
            raise TimeoutError('Windows setup/qualification exceeded its bounded host deadline.')
    except (Exception, KeyboardInterrupt) as error:
        failure = str(error)
        if isinstance(error, subprocess.CalledProcessError):
            failure += '\n' + error.stderr.replace(password, '[redacted]')
        (results / 'host-error.txt').write_text(failure + '\n')
        print(failure, flush=True)
    finally:
        if owned_work:
            try:
                if compose_written:
                    logs = command(*compose, 'logs', '--no-color', '--tail', '1500', check=False).stdout
                    (results / 'vm.log').write_text(logs.replace(password, '[redacted]'))
                    command(*compose, 'down', '--timeout', '30')
                label = f'label=com.docker.compose.project={project}'
                cleanup['containersRemoved'] = not command('docker', 'ps', '-aq', '--filter', label).stdout.strip()
                cleanup['networkRemoved'] = not command('docker', 'network', 'ls', '-q', '--filter', label).stdout.strip()
                if cleanup['containersRemoved'] and cleanup['networkRemoved']:
                    shutil.rmtree(work)
                    cleanup['stateRemoved'] = not work.exists()
                if pulled_image and not image_preexisting:
                    command('docker', 'image', 'rm', IMAGE)
                    cleanup['imageRemoved'] = True
                else:
                    cleanup['imageRemoved'] = True  # No image owned by this run remains.
                    cleanup['preexistingImageRetained'] = image_preexisting
            except Exception as error:
                cleanup['error'] = str(error).replace(password, '[redacted]')
        else:
            cleanup.update(containersRemoved=True, networkRemoved=True, stateRemoved=True, imageRemoved=True)
        (results / 'cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')
    if failure or not all(cleanup[key] for key in ['containersRemoved', 'networkRemoved', 'stateRemoved', 'imageRemoved']):
        raise SystemExit(1)


if __name__ == '__main__':
    main()
