"""Measure a clean selected checkout and fresh public packages without changing engines."""
import argparse
import copy
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import urllib.request
from xml.sax.saxutils import escape
from zipfile import ZipFile


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def save(path, value):
    Path(path).write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def run(command, cwd, log, allowed=(0,)):
    with Path(log).open('w', encoding='utf-8') as stream:
        result = subprocess.run([str(c) for c in command], cwd=cwd, stdout=stream,
                                stderr=subprocess.STDOUT, check=False)
    if result.returncode not in allowed:
        raise RuntimeError(f'Command exited {result.returncode}: {command}; see {log}')
    return dict(command=[str(c) for c in command], exitCode=result.returncode,
                log=Path(log).name, logSha256=sha(log))


def compact(report):
    value = copy.deepcopy(report)
    for result in value['results']:
        measurement = result.get('measurement', {})
        snapshot = measurement.pop('snapshot', None)
        if snapshot:
            measurement['pageSummary'] = [dict(width=p['width'], height=p['height'], text=p['text'],
                                              glyphCount=len(p['glyphs'])) for p in snapshot['pages']]
    return value


def verify_measurements(report, defects, comparison, expected, binaries):
    comparison.validate_defect_report(report, defects)
    if len(report['results']) != expected or len({r['documentId'] for r in report['results']}) != expected:
        raise ValueError('Missing or duplicate document measurements')
    for result in report['results']:
        if result['status'] != 'measured' or len(result.get('referenceChecks', [])) != 1 or \
                result['referenceChecks'][0]['status'] != 'compared':
            raise ValueError('Incomplete measurement or independent reference')
        loaded = {a['name'] + '.dll': a['sha256'] for a in result['measurement']['assemblies']}
        for name in ('wordnet.dll', 'wordnet.Fonts.dll'):
            # Explicit caller fonts can avoid loading the optional embedded resource assembly.
            if name == 'wordnet.Fonts.dll' and name not in loaded:
                continue
            if loaded.get(name) != binaries[name]:
                raise ValueError('Measured worker loaded unexpected binary: ' + name)
    return sum(bool(comparison.failure_verdicts(r)) for r in report['results'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', type=Path, required=True, help='Clean checkout of the measured revision')
    parser.add_argument('--commit', required=True)
    parser.add_argument('--output', type=Path, required=True, help='New, empty evidence directory')
    parser.add_argument('--reductions', type=Path, help='Additional independently authored manifest')
    parser.add_argument('--iterations', type=int, default=3)
    args = parser.parse_args()
    root, output = args.repository.resolve(), args.output.resolve()
    if output.exists() and any(output.iterdir()):
        parser.error('Choose a fresh output directory; previous evidence must not be overwritten.')
    if not 1 <= args.iterations <= 100:
        parser.error('iterations must be 1..100')
    revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
    dirty = subprocess.check_output(['git', 'status', '--porcelain'], cwd=root, text=True).strip()
    if revision != args.commit or dirty:
        parser.error('Measured checkout must be clean and exactly match --commit.')
    output.mkdir(parents=True, exist_ok=True)
    spec = importlib.util.spec_from_file_location('comparison', root / 'eng/run-comparison.py')
    comparison = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(comparison)
    commands = []
    receipt = dict(schemaVersion=1, selectedSourceCommit=revision, sourceCheckoutClean=True,
                   selectedSourceTree=subprocess.check_output(['git', 'rev-parse', 'HEAD^{tree}'], cwd=root, text=True).strip(),
                   measuredAtUtc=datetime.now(timezone.utc).isoformat(), runnerSha256=sha(__file__),
                   publicPackages={}, commands=commands, cohorts={})
    save(output / 'progress.json', receipt)
    archives = output / 'packages'
    archives.mkdir()
    indexes = {}
    for name in ('wordnet', 'wordnet.fonts', 'wordnet.ocr'):
        url = f'https://api.nuget.org/v3-flatcontainer/{name}/index.json'
        with urllib.request.urlopen(url, timeout=60) as response:
            data = response.read()
        path = archives / (name + '-index.json')
        path.write_bytes(data)
        indexes[name] = json.loads(data)['versions']
        receipt['publicPackages'][name] = dict(indexUrl=url, indexSha256=sha(path),
                                             versions=indexes[name])
    stable = [v for v in indexes['wordnet'] if re.fullmatch(r'\d+(?:\.\d+){2,3}', v)]
    version = max(stable, key=lambda v: tuple(int(x) for x in v.split('.')))
    receipt['publicPackageVersion'] = version
    for name in ('wordnet', 'wordnet.fonts'):
        if version not in indexes[name]:
            raise RuntimeError('Matching public dependency version is unavailable: ' + name)
        url = f'https://api.nuget.org/v3-flatcontainer/{name}/{version}/{name}.{version}.nupkg'
        path = archives / f'{name}.{version}.nupkg'
        with urllib.request.urlopen(url, timeout=120) as response:
            path.write_bytes(response.read())
        with ZipFile(path) as archive:
            entries = {n: hashlib.sha256(archive.read(n)).hexdigest()
                       for n in archive.namelist() if not n.endswith('/')}
            nuspec = archive.read(next(n for n in entries if n.endswith('.nuspec'))).decode('utf-8-sig')
        receipt['publicPackages'][name].update(version=version, archiveUrl=url,
            archiveSha256=sha(path), entries=entries, nuspec=nuspec)
    save(output / 'progress.json', receipt)
    worker_project = root / 'tools/wordnet.Benchmarks/wordnet.Benchmarks.csproj'
    commands.append(run(['dotnet', 'restore', worker_project, '--locked-mode'], root, output / 'source-restore.log'))
    commands.append(run(['dotnet', 'build', worker_project, '-c', 'Release', '--no-restore'], root, output / 'source-build.log'))
    source_worker = root / 'tools/wordnet.Benchmarks/bin/Release/net10.0/wordnet.Benchmarks.dll'
    package_project = output / 'package-project'
    package_project.mkdir()
    for name in ('global.json', 'Directory.Build.props', 'Directory.Packages.props'):
        shutil.copyfile(root / name, package_project / name)
    for source in worker_project.parent.glob('*.cs'):
        shutil.copyfile(source, package_project / source.name)
    (package_project / 'wordnet.Benchmarks.csproj').write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
        '<OutputType>Exe</OutputType><IsPackable>false</IsPackable></PropertyGroup><ItemGroup>'
        f'<PackageReference Include="wordnet" VersionOverride="{version}"/>'
        '<PackageReference Include="BenchmarkDotNet"/></ItemGroup></Project>', encoding='utf-8')
    config = package_project / 'NuGet.Config'
    config.write_text('<configuration><packageSources><clear/>'
        f'<add key="public-archives" value="{escape(str(archives))}"/>'
        '<add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources>'
        '<packageSourceMapping><packageSource key="public-archives"><package pattern="wordnet"/>'
        '<package pattern="wordnet.Fonts"/></packageSource><packageSource key="nuget.org">'
        '<package pattern="*"/></packageSource></packageSourceMapping></configuration>', encoding='utf-8')
    cache = output / 'package-cache'
    project = package_project / 'wordnet.Benchmarks.csproj'
    commands.append(run(['dotnet', 'restore', project, '--configfile', config, '--packages', cache],
                        package_project, output / 'package-restore.log'))
    commands.append(run(['dotnet', 'restore', project, '--locked-mode', '--configfile', config, '--packages', cache],
                        package_project, output / 'package-locked-restore.log'))
    commands.append(run(['dotnet', 'build', project, '-c', 'Release', '--no-restore'],
                        package_project, output / 'package-build.log'))
    package_worker = package_project / 'bin/Release/net10.0/wordnet.Benchmarks.dll'
    receipt['packageConsumerLock'] = json.loads((package_project / 'packages.lock.json').read_text())
    receipt['packageConsumerLockSha256'] = sha(package_project / 'packages.lock.json')
    receipt['workers'] = {mode: dict(path=str(worker), sha256=sha(worker),
        binaries={p.name: sha(p) for p in sorted(worker.parent.glob('*.dll'))})
        for mode, worker in (('source', source_worker), ('package', package_worker))}
    for name, asset in (('wordnet', 'lib/net10.0/wordnet.dll'),
                        ('wordnet.fonts', 'lib/netstandard2.0/wordnet.Fonts.dll')):
        if receipt['publicPackages'][name]['entries'][asset] != receipt['workers']['package']['binaries'][Path(asset).name]:
            raise RuntimeError('Package worker did not load the downloaded public asset: ' + name)
    commands.append(run([sys.executable, root / 'eng/test-comparison.py'], root, output / 'comparison-controls.log'))
    manifests = [('existing-24', root / 'tests/wordnet.Conversion.Tests/Fixtures/Comparison/manifest.json')]
    if args.reductions:
        manifests.append(('reductions', args.reductions.resolve()))
    for cohort, manifest in manifests:
        office = output / (cohort + '-office')
        commands.append(run([sys.executable, root / 'eng/capture-office-comparison.py', '--cohort', 'all',
            '--manifest', manifest, '--output', office], root, output / (cohort + '-capture.log')))
        references = json.loads((office / 'references.json').read_text())
        cohort_receipt = dict(manifest=json.loads(manifest.read_text()), manifestSha256=sha(manifest),
                              references=references, measurements={}, defects={})
        receipt['cohorts'][cohort] = cohort_receipt
        for mode, worker in (('source', source_worker), ('package', package_worker)):
            destination = output / (cohort + '-' + mode)
            command = run([sys.executable, root / 'eng/run-comparison.py', '--cohort', 'all',
                '--manifest', manifest, '--worker', worker, '--iterations', args.iterations,
                '--poi-classpath', root / 'artifacts/comparison-poi', '--references', office / 'references.json',
                '--output', destination], root, output / (cohort + '-' + mode + '.log'), allowed=(0, 1))
            commands.append(command)
            report = json.loads((destination / 'results.json').read_text())
            defects = json.loads((destination / 'defects.json').read_text())
            expected = len(cohort_receipt['manifest']['documents'])
            failures = verify_measurements(report, defects, comparison, expected,
                                           receipt['workers'][mode]['binaries'])
            if command['exitCode'] != (1 if failures else 0):
                raise RuntimeError('Runner exit status does not match retained verdicts')
            cohort_receipt['measurements'][mode] = compact(report)
            cohort_receipt['defects'][mode] = defects
            cohort_receipt.setdefault('rawHashes', {})[mode] = dict(results=sha(destination / 'results.json'),
                defects=sha(destination / 'defects.json'))
            save(output / 'progress.json', receipt)
    receipt['completedAtUtc'] = datetime.now(timezone.utc).isoformat()
    if subprocess.check_output(['git', 'status', '--porcelain'], cwd=root, text=True).strip():
        raise RuntimeError('Measured checkout changed during the run; inspect retained evidence')
    save(output / 'receipt.json', receipt)
    print('Complete measurements with original verdicts retained: ' + str(output / 'receipt.json'))


if __name__ == '__main__':
    main()
