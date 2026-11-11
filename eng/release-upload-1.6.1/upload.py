import hashlib
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

REPO = 'kirakosyan/word.net'
COMMIT = 'e2dadeaef361aa1651bf37d1c9325d43de4d4bb2'
CANDIDATE = '5386f97dae0f00743f489bf7d09d6df800b6c095'
RELEASE_ID = 442171847
root = Path('artifacts/release-upload')
data = Path('eng/release-upload-1.6.1')
assets = root / 'assets'
assets.mkdir()


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def gh(*args):
    return subprocess.run(['gh', *args], check=True, capture_output=True, text=True).stdout


def api(path):
    return json.loads(gh('api', f'repos/{REPO}/{path}'))


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


assert os.environ['GITHUB_REPOSITORY'] == REPO
assert os.environ['GITHUB_REF'] == 'refs/heads/release/upload-1.6.1'
runs = {}
for name, run_id, commit in [('main', 43083836263, COMMIT), ('publication', 43091742624, COMMIT), ('candidate', 43077286116, CANDIDATE)]:
    run = api(f'actions/runs/{run_id}')
    assert run['conclusion'] == 'success' and run['head_sha'] == commit
    if name != 'candidate':
        assert run['head_branch'] == 'main'
    runs[name] = run
    (root / f'{name}-run.json').write_text(json.dumps(run, indent=2) + '\n')

for entry in read(data / 'packages.json'):
    path = root / 'main/nuget-packages' / entry['package']
    assert entry['commit'] == COMMIT and digest(path) == entry['archiveSha256'], path
    shutil.copy2(path, assets / path.name)
assert len(list(assets.glob('*.*nupkg'))) == 6

for parent, pattern, count in [(root / 'main', 'tests-*/*.trx', 9), (root / 'main', 'packaged-tests-*/*.trx', 17), (root / 'publication', 'release-tests-*/*.trx', 9)]:
    reports = list(parent.glob(pattern))
    assert len(reports) == count, (pattern, len(reports))
    for report in reports:
        counters = next(e for e in ET.parse(report).iter() if e.tag.rsplit('}', 1)[-1] == 'Counters').attrib
        assert all(int(counters[key]) == 2511 for key in ['total', 'passed', 'executed']), report
        assert all(int(value) == 0 for key, value in counters.items() if key not in ['total', 'passed', 'executed']), report

validate = runpy.run_path('eng/minimum-runtimes/run-linux-vm.py')['validate_results']
minimum = []
for parent, commit in [('main', COMMIT), ('candidate', CANDIDATE)]:
    for asset in ['source', 'package']:
        path = root / parent / f'minimum-framework472-{asset}'
        bundle = read(path / 'bundle.json')
        assert bundle['sourceCommit'] == commit and bundle['requiredCases'] == 2511 and bundle['testAsset'] == asset
        minimum.append(validate(path, bundle))
        cleanup = read(path / 'cleanup.json')
        assert all(cleanup.get(k) is True for k in ['containersRemoved', 'networkRemoved', 'stateRemoved', 'imageRemoved']) and not cleanup.get('error')
        if parent == 'main' and asset == 'package':
            for name in ['wordnet', 'wordnet.Fonts', 'wordnet.Ocr']:
                tfm = 'netstandard2.0' if name == 'wordnet.Fonts' else 'net472'
                with zipfile.ZipFile(assets / f'{name}.1.6.1.nupkg') as archive:
                    assert hashlib.sha256(archive.read(f'lib/{tfm}/{name}.dll')).hexdigest() == bundle['binaries'][f'{name}.dll']

public = read(data / 'public-packages.json')
assert len(public) == 3 and all(item['commit'] == COMMIT and item['version'] == '1.6.1' for item in public)
consumer = read(data / 'public-consumer.json')
assert consumer['commit'] == COMMIT and len(consumer['reports']) == 2
assert all(item['passed'] and item['binariesMatchImmutablePackages'] for item in consumer['reports'])

evidence = []
for folder in ['main', 'publication', 'candidate']:
    for path in sorted((root / folder).rglob('*')):
        if not path.is_file():
            continue
        relative = path.relative_to(root)
        if any(part in ['framework-source-vm', 'framework-package-vm', 'nuget-packages'] for part in relative.parts):
            continue
        evidence.append((path, str(relative)))
evidence += [(path, f'public-verification/{path.relative_to(data)}') for path in sorted(data.rglob('*')) if path.is_file()]
evidence += [(root / f'{name}-run.json', f'{name}-run.json') for name in runs]
manifest = {name: digest(path) for path, name in evidence}
qualification = assets / 'wordnet-1.6.1-qualification.zip'
with zipfile.ZipFile(qualification, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
    for path, name in evidence:
        archive.write(path, name)
    archive.writestr('evidence-sha256.json', json.dumps(manifest, indent=2) + '\n')
    archive.writestr('minimum-runtime-verification.json', json.dumps(minimum, indent=2) + '\n')
with zipfile.ZipFile(qualification) as archive:
    assert archive.testzip() is None
    assert all(hashlib.sha256(archive.read(name)).hexdigest() == expected for name, expected in manifest.items())

hashes = {path.name: digest(path) for path in sorted(assets.iterdir())}
(assets / 'SHA256SUMS.txt').write_text(''.join(f'{value}  {name}\n' for name, value in hashes.items()))
hashes['SHA256SUMS.txt'] = digest(assets / 'SHA256SUMS.txt')
if os.environ.get('WORDNET_RELEASE_VERIFY_ONLY') == '1':
    print(json.dumps({'verifiedOnly': True, 'assets': len(hashes), 'evidenceFiles': len(manifest), 'minimumRuntime': minimum}))
    raise SystemExit(0)
release = api(f'releases/{RELEASE_ID}')
assert release['tag_name'] == 'v1.6.1' and release['target_commitish'] == COMMIT
existing = {item['name']: item for item in release['assets']}
for name, value in hashes.items():
    if name in existing:
        assert existing[name]['digest'] == f'sha256:{value}' and existing[name]['state'] == 'uploaded'
    else:
        gh('release', 'upload', 'v1.6.1', str(assets / name), '--repo', REPO)

release = api(f'releases/{RELEASE_ID}')
assert len(release['assets']) == len(hashes)
assert all(item['digest'] == f"sha256:{hashes[item['name']]}" and item['state'] == 'uploaded' for item in release['assets'])
payload = root / 'publish.json'
payload.write_text(json.dumps({'draft': False, 'body': (data / 'notes.md').read_text(), 'target_commitish': COMMIT, 'make_latest': 'true'}))
release = json.loads(gh('api', '--method', 'PATCH', f'repos/{REPO}/releases/{RELEASE_ID}', '--input', str(payload)))
tag = api('git/ref/tags/v1.6.1')
assert not release['draft'] and not release['prerelease'] and tag['object']['sha'] == COMMIT
receipt = {'releaseUrl': release['html_url'], 'releaseId': RELEASE_ID, 'sourceCommit': COMMIT, 'uploadRun': os.environ['GITHUB_RUN_ID'], 'uploadWorkflowCommit': os.environ['GITHUB_SHA'], 'assets': release['assets'], 'hashes': hashes, 'evidenceFiles': len(manifest), 'minimumRuntime': minimum, 'published': True}
(root / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
print(json.dumps({'published': True, 'url': release['html_url'], 'assets': len(hashes), 'sourceCommit': COMMIT}))
