"""Compare unchanged-parent and candidate workers without altering historical evidence."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--before-worker', type=Path, required=True)
    parser.add_argument('--after-worker', type=Path, required=True)
    parser.add_argument('--probes', type=Path, required=True, help='WORDNET_ROADMAP_EVIDENCE from the P1 test run')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    manifest_path = ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/Comparison/manifest.json'
    manifest = json.loads(manifest_path.read_text())
    fonts = args.output / 'fonts'
    fonts.mkdir(exist_ok=True)
    for font in manifest['fonts']:
        source = ROOT / font['path']
        if sha(source) != font['sha256']:
            raise ValueError('Font identity mismatch: ' + str(source))
        shutil.copyfile(source, fonts / source.name)
    documents = [(row['id'], manifest_path.parent / row['path'], row) for row in manifest['documents']]
    documents += [(p.stem, p, None) for p in sorted(args.probes.glob('p1-*.docx'))]
    if len(documents) < 36:
        raise ValueError('Run all P1 layout/story evidence tests before measuring.')
    receipt = dict(schemaVersion=1, parentCommit=subprocess.check_output(['git', '-C', str(ROOT), 'rev-parse', 'origin/main'], text=True).strip(),
                   candidateCommit=subprocess.check_output(['git', '-C', str(ROOT), 'rev-parse', 'HEAD'], text=True).strip(),
                   candidateDirty=bool(subprocess.check_output(['git', '-C', str(ROOT), 'status', '--porcelain'], text=True).strip()),
                   provenance='Original MIT OOXML probes from P1 tests and unchanged 24-document corpus; spec/font metrics are the independent expectations. No fresh office/raster qualification is inferred.',
                   fonts=manifest['fonts'], manifestSha256=sha(manifest_path), workers={}, results=[])
    env = os.environ.copy()
    env['DOTNET_PROCESSOR_COUNT'] = '2'
    for mode, worker in [('before', args.before_worker), ('after', args.after_worker)]:
        receipt['workers'][mode] = dict(workerSha256=sha(worker), coreSha256=sha(worker.with_name('wordnet.dll')))
        for name, source, authored in documents:
            if authored and sha(source) != authored['sha256']:
                raise ValueError('Input identity mismatch: ' + name)
            directory = args.output / mode
            directory.mkdir(exist_ok=True)
            pdf, report = directory / (name + '.pdf'), directory / (name + '.json')
            command = ['dotnet', str(worker.resolve()), '--comparison', 'wordnet', str(source.resolve()), str(pdf.resolve()), str(report.resolve()), str(fonts.resolve()), '1', '-']
            run = subprocess.run(command, env=env, capture_output=True, text=True)
            row = next((r for r in receipt['results'] if r['id'] == name), None)
            if row is None:
                row = dict(id=name, inputSha256=sha(source), originalCorpus=authored is not None)
                receipt['results'].append(row)
            if run.returncode:
                row[mode] = dict(status='failed', exitCode=run.returncode, error=run.stderr.strip())
                continue
            data = json.loads(report.read_text())
            snapshot_path = directory / (name + '.snapshot.json')
            subprocess.run(['dotnet', str(worker.resolve()), '--comparison', 'snapshot', str(pdf.resolve()), str(snapshot_path.resolve())], env=env, check=True, capture_output=True)
            snapshot = json.loads(snapshot_path.read_text())
            text = ' '.join(' '.join(p['text'].split()) for p in snapshot['pages'])
            required = authored['requiredText'] if authored else []
            position, ordered = 0, True
            for expected in required:
                found = text.find(' '.join(expected.split()), position)
                if found < 0:
                    ordered = False
                    break
                position = found + len(' '.join(expected.split()))
            row[mode] = dict(status='measured', reportSha256=sha(report), snapshotSha256=sha(snapshot_path), outputSha256=sha(pdf),
                             pageCount=snapshot['pageCount'], authoredTextAndOrder=ordered if authored else None,
                             authoredPageCount=snapshot['pageCount'] == authored['expectedPages'] if authored else None,
                             diagnostics=data['diagnostics'], runtime=data['runtime'], architecture=data['architecture'])
            if not authored:
                row[mode]['snapshot'] = snapshot
    for row in receipt['results']:
        before, after = row['before'], row['after']
        if before['status'] != 'measured' or after['status'] != 'measured':
            continue
        row['paginationChanged'] = before['pageCount'] != after['pageCount']
        left = json.loads((args.output / 'before' / (row['id'] + '.snapshot.json')).read_text())
        right = json.loads((args.output / 'after' / (row['id'] + '.snapshot.json')).read_text())
        row['samePageText'] = [p['text'] for p in left['pages']] == [p['text'] for p in right['pages']]
        old = [g for p in left['pages'] for g in p['glyphs']]
        new = [g for p in right['pages'] for g in p['glyphs']]
        same = [g['text'] for g in old] == [g['text'] for g in new]
        row['glyphSequenceMatches'] = same
        row['maximumSourceChangePoints'] = max((max(abs(a['x'] - b['x']), abs(a['y'] - b['y'])) for a, b in zip(old, new)), default=0) if same else None
    (args.output / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print(f"Measured {len(documents)} documents per worker; failures remain in {args.output / 'receipt.json'}")
    return 1 if any(r[m]['status'] == 'failed' for r in receipt['results'] for m in ('before', 'after')) else 0


if __name__ == '__main__':
    raise SystemExit(main())
