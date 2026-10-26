"""Measure a clean selected checkout and fresh public packages without changing engines."""
import argparse
import copy
from datetime import datetime, timezone
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import platform
import re
import shutil
import statistics
import subprocess
import sys
import urllib.request
from xml.sax.saxutils import escape, quoteattr
from xml.etree import ElementTree
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]
REGISTRATION = 'https://api.nuget.org/v3/registration5-gz-semver2/wordnet/index.json'


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def decode_json(data):
    # NuGet's registration5-gz endpoint serves gzip even without Accept-Encoding.
    return json.loads(gzip.decompress(data) if data.startswith(b'\x1f\x8b') else data)


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


def verify_poi(directory, pins=None):
    pins = pins if pins is not None else json.loads((ROOT / 'eng/comparison-poi.json').read_text())
    for pin in pins:
        jar = directory / pin['name']
        if not jar.is_file() or sha(jar) != pin['sha256']:
            raise ValueError('Missing or changed POI jar: ' + str(jar) + '; run eng/prepare-comparison-poi.py with this directory.')
    return {pin['name']: pin['sha256'] for pin in pins}


def latest_listed_stable(fetch):
    versions = []
    index = fetch(REGISTRATION)
    for page in index['items']:
        leaves = page['items'] if 'items' in page else fetch(page['@id'])['items']
        for leaf in leaves:
            entry = leaf['catalogEntry']
            version = entry['version']
            if entry.get('listed') is True and re.fullmatch(r'\d+(?:\.\d+){2,3}', version):
                versions.append(version)
    if not versions:
        raise ValueError('NuGet registration contains no listed stable wordnet version.')
    return max(versions, key=lambda v: tuple(int(x) for x in v.split('.')))


def prepare_package_project(root, destination, archives, version):
    destination.mkdir()
    for name in ('global.json', 'Directory.Build.props'):
        shutil.copyfile(root / name, destination / name)
    packages = ElementTree.parse(root / 'Directory.Packages.props')
    benchmark = next(p.attrib['Version'] for p in packages.iter('PackageVersion')
                     if p.attrib.get('Include') == 'BenchmarkDotNet')
    # Shadow parent central props: only the development tool is pinned here.
    (destination / 'Directory.Packages.props').write_text(
        '<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>'
        '<CentralPackageTransitivePinningEnabled>false</CentralPackageTransitivePinningEnabled>'
        '</PropertyGroup><ItemGroup><PackageVersion Include="BenchmarkDotNet" '
        f'Version="{escape(benchmark)}"/></ItemGroup></Project>', encoding='utf-8')
    for source in (root / 'tools/wordnet.Benchmarks').glob('*.cs'):
        shutil.copyfile(source, destination / source.name)
    project = destination / 'wordnet.Benchmarks.csproj'
    project.write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
        '<OutputType>Exe</OutputType><IsPackable>false</IsPackable></PropertyGroup><ItemGroup>'
        f'<PackageReference Include="wordnet" VersionOverride="{escape(version)}"/>'
        '<PackageReference Include="BenchmarkDotNet"/></ItemGroup></Project>', encoding='utf-8')
    config = destination / 'NuGet.Config'
    config.write_text('<configuration><packageSources><clear/>'
        f'<add key="public-archives" value={quoteattr(str(archives))}/>'
        '<add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources>'
        '<packageSourceMapping><packageSource key="public-archives"><package pattern="wordnet"/>'
        '<package pattern="wordnet.Fonts"/></packageSource><packageSource key="nuget.org">'
        '<package pattern="*"/></packageSource></packageSourceMapping></configuration>', encoding='utf-8')
    return project, config


def summarize(receipt):
    summary = {}
    for name, cohort in receipt['cohorts'].items():
        aggregate = {}
        for mode, report in cohort['measurements'].items():
            results = report['results']
            checks = [r['referenceChecks'][0] for r in results]
            values = dict(documents=len(results),
                referenceFailures=sum(any(not c[key] for key in ('pageCountMatches', 'pageGeometryMatches',
                    'textAndOrderMatches', 'glyphPlacementPasses', 'textAndGeometryPassed', 'visualPassed')) for c in checks),
                authoredContentPasses=sum(r['authoredChecks']['passed'] for r in results),
                authoredPagePasses=sum(r['authoredChecks']['pageCountMatches'] for r in results),
                editableContentPasses=sum(r['editableContent']['passed'] for r in results),
                diagnosticPasses=sum(r['knownLossChecks']['passed'] for r in results))
            for field, verdict in (('referencePagePasses', 'pageCountMatches'), ('referenceTextPasses', 'textAndOrderMatches'),
                                   ('referenceGlyphPasses', 'glyphPlacementPasses'), ('referenceGeometryPasses', 'pageGeometryMatches'),
                                   ('referenceRasterPasses', 'visualPassed')):
                values[field] = sum(c[verdict] for c in checks)
            samples = {}
            for result in results:
                for sample in result['measurement']['samples']:
                    samples.setdefault(sample['kind'], []).append(sample['milliseconds'])
            values['timing'] = {kind: dict(count=len(times), minimum=min(times), median=statistics.median(times), maximum=max(times))
                                for kind, times in samples.items()}
            aggregate[mode] = values
        source = {r['documentId']: r for r in cohort['measurements']['source']['results']}
        package = {r['documentId']: r for r in cohort['measurements']['package']['results']}
        # For compact receipts, identical PDF hashes establish identical snapshot-reader
        # input bytes; full reports can additionally compare the complete glyph snapshots.
        aggregate['sourcePackageSnapshotsAndVerdictsIdentical'] = source.keys() == package.keys() and all(
            source[key]['measurement']['outputSha256'] == package[key]['measurement']['outputSha256'] and
            source[key]['measurement'].get('snapshot', source[key]['measurement'].get('pageSummary')) ==
            package[key]['measurement'].get('snapshot', package[key]['measurement'].get('pageSummary')) and
            all(source[key][field] == package[key][field] for field in ('inputSha256', 'status', 'authoredChecks',
                'knownLossChecks', 'editableContent', 'referenceChecks')) for key in source)
        summary[name] = aggregate
    return summary


def audit_evidence(receipt, output, comparison):
    for name, cohort in receipt['cohorts'].items():
        for reference in cohort['references']['references']:
            if sha(output / (name + '-office') / reference['pdf']) != reference['sha256']:
                raise ValueError('Independent reference hash mismatch: ' + reference['documentId'])
        for mode, report in cohort['measurements'].items():
            directory = output / (name + '-' + mode)
            raw = json.loads((directory / 'results.json').read_text())
            defects = json.loads((directory / 'defects.json').read_text())
            for kind in ('results', 'defects'):
                if sha(directory / (kind + '.json')) != cohort['rawHashes'][mode][kind]:
                    raise ValueError('Raw report hash mismatch: ' + str(directory))
            verify_measurements(raw, defects, comparison, len(cohort['manifest']['documents']), receipt['workers'][mode]['binaries'])
            if compact(raw) != report or defects != cohort['defects'][mode]:
                raise ValueError('Compact receipt differs from retained raw evidence.')
    for name, asset in (('wordnet', 'lib/net10.0/wordnet.dll'), ('wordnet.fonts', 'lib/netstandard2.0/wordnet.Fonts.dll')):
        if receipt['publicPackages'][name]['entries'][asset] != receipt['workers']['package']['binaries'][Path(asset).name]:
            raise ValueError('Package asset differs from the downloaded archive: ' + name)
    return dict(allMeasuredBinariesMatchWorkers=True, packageAssetsMatchDownloadedArchives=True,
                everyIndependentReferenceHashVerified=True, compactReportsMatchRawReports=True,
                independentOpenXmlSchemas=dict(status='not-run', reason='This runner does not execute the separate Open XML schema validator.'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--summarize', type=Path, help='Print summary tables/statistics from an existing raw or compact receipt, without measuring')
    parser.add_argument('--repository', type=Path, help='Clean checkout of the measured revision')
    parser.add_argument('--commit')
    parser.add_argument('--output', type=Path, help='New, empty evidence directory')
    parser.add_argument('--reductions', type=Path, help='Additional independently authored manifest')
    parser.add_argument('--poi-classpath', type=Path, default=ROOT / 'artifacts/comparison-poi',
                        help='Pinned POI jars prepared in the runner checkout (or an explicit directory)')
    parser.add_argument('--iterations', type=int, default=3)
    args = parser.parse_args()
    if args.summarize:
        print(json.dumps(summarize(json.loads(args.summarize.read_text())), indent=2))
        return
    if not all((args.repository, args.commit, args.output)):
        parser.error('Measurement requires --repository, --commit and --output.')
    poi = args.poi_classpath.resolve()
    try:
        poi_hashes = verify_poi(poi)
    except ValueError as error:
        parser.error(str(error))
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
                   publicPackages={}, commands=commands, cohorts={}, poiClasspath=str(poi), poiJars=poi_hashes)
    save(output / 'progress.json', receipt)
    archives = output / 'packages'
    archives.mkdir()
    listing_evidence = []
    def fetch_registration(url):
        with urllib.request.urlopen(url, timeout=60) as response:
            data = response.read()
        suffix = '.json.gz' if data.startswith(b'\x1f\x8b') else '.json'
        path = archives / f'wordnet-registration-{len(listing_evidence)}{suffix}'
        path.write_bytes(data)
        listing_evidence.append(dict(url=url, file=path.name, sha256=sha(path)))
        return decode_json(data)
    version = latest_listed_stable(fetch_registration)
    receipt['packageSelection'] = dict(policy='latest-listed-stable', registration=listing_evidence)
    indexes = {}
    for name in ('wordnet', 'wordnet.fonts', 'wordnet.ocr'):
        url = f'https://api.nuget.org/v3-flatcontainer/{name}/index.json'
        with urllib.request.urlopen(url, timeout=60) as response:
            data = response.read()
        path = archives / (name + '-index.json')
        path.write_bytes(data)
        indexes[name] = decode_json(data)['versions']
        receipt['publicPackages'][name] = dict(indexUrl=url, indexSha256=sha(path),
                                             versions=indexes[name])
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
    project, config = prepare_package_project(root, package_project, archives, version)
    cache = output / 'package-cache'
    commands.append(run(['dotnet', 'restore', project, '--configfile', config, '--packages', cache],
                        package_project, output / 'package-restore.log'))
    commands.append(run(['dotnet', 'restore', project, '--locked-mode', '--configfile', config, '--packages', cache],
                        package_project, output / 'package-locked-restore.log'))
    commands.append(run(['dotnet', 'build', project, '-c', 'Release', '--no-restore'],
                        package_project, output / 'package-build.log'))
    package_worker = package_project / 'bin/Release/net10.0/wordnet.Benchmarks.dll'
    receipt['packageConsumerLock'] = json.loads((package_project / 'packages.lock.json').read_text())
    if any(p['type'] == 'CentralTransitive' for group in receipt['packageConsumerLock']['dependencies'].values() for p in group.values()):
        raise RuntimeError('Public consumer inherited central transitive pins.')
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
                '--poi-classpath', poi, '--references', office / 'references.json',
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
    receipt['summary'] = summarize(receipt)
    receipt['audit'] = audit_evidence(receipt, output, comparison)
    receipt['audit']['measuredCheckoutStillClean'] = True
    controls = (output / 'comparison-controls.log').read_text()
    receipt['audit']['comparisonControlsPassed'] = int(re.search(r'Ran (\d+) tests?', controls).group(1))
    commands.append(run(['dotnet', '--info'], root, output / 'dotnet-info.log'))
    receipt['environment'] = dict(python=sys.version, platform=platform.platform(), machine=platform.machine(),
        kernel=platform.uname()._asdict(), dotnetInfo=(output / 'dotnet-info.log').read_text(),
        scope='Observed process/host only; CPU pinning, host isolation and container-image identity are not established.')
    if shutil.which('dpkg-query'):
        commands.append(run(['dpkg-query', '-W'], root, output / 'installed-packages.log'))
        receipt['environment']['installedPackagesSha256'] = sha(output / 'installed-packages.log')
    save(output / 'receipt.json', receipt)
    print('Complete measurements with original verdicts retained: ' + str(output / 'receipt.json'))


if __name__ == '__main__':
    main()
