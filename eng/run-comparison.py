"""Reproduce per-document Word/Aspose/wordnet metrics; commercial tools are optional."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
TOLERANCES = dict(glyphPoints=0.25, significantPixelChannelSum=96, changedPixelFraction=0.002, meanChannelError=0.25)


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def normalized(text):
    return ' '.join(text.split())


def renderer_identity(banner):
    return '\n'.join(banner.strip().splitlines())


def loss_check(expected, diagnostics):
    # Information is advisory; every warning requires an explicit corpus expectation.
    expected = set(expected)
    observed = {d['code'] for d in diagnostics if d['severity'] == 'Warning'}
    return dict(passed=observed == expected, missing=sorted(expected - observed), unexpected=sorted(observed - expected))


def content_check(snapshot, expected):
    """Require every authored text fragment in reading order; no content-loss allowance."""
    text = normalized(' '.join(p['text'] for p in snapshot['pages']))
    cursor = 0
    missing = []
    for item in expected:
        position = text.find(normalized(item), cursor)
        if position < 0:
            missing.append(item)
        else:
            cursor = position + len(normalized(item))
    return dict(passed=not missing, missingOrOutOfOrder=missing)


def snapshot_check(actual, reference):
    page_match = actual['pageCount'] == reference['pageCount']
    text_match = [normalized(p['text']) for p in actual['pages']] == [normalized(p['text']) for p in reference['pages']]
    maximum = 0.0
    glyph_match = page_match
    geometry_match = page_match
    for a, b in zip(actual['pages'], reference['pages']):
        geometry_match &= abs(a['width'] - b['width']) <= 0.01 and abs(a['height'] - b['height']) <= 0.01
        # Match identical glyph sequences only; never align away missing characters.
        if [g['text'] for g in a['glyphs']] != [g['text'] for g in b['glyphs']]:
            glyph_match = False
            continue
        for ga, gb in zip(a['glyphs'], b['glyphs']):
            maximum = max(maximum, abs(ga['x'] - gb['x']), abs(ga['y'] - gb['y']))
    return dict(pageCountMatches=page_match, pageGeometryMatches=bool(geometry_match), textAndOrderMatches=text_match,
                glyphSequenceMatches=glyph_match, maximumGlyphDisplacementPoints=maximum,
                glyphPlacementPasses=glyph_match and maximum <= TOLERANCES['glyphPoints'])


def pixel_check(actual, reference):
    from PIL import Image, ImageChops
    with Image.open(actual) as a, Image.open(reference) as b:
        if a.size != b.size:
            return dict(passed=False, dimensionsMatch=False)
        difference = ImageChops.difference(a.convert('RGB'), b.convert('RGB'))
        pixels = a.width * a.height
        changed = total = 0
        for r, g, blue in difference.get_flattened_data():
            delta = r + g + blue
            total += delta
            changed += delta > TOLERANCES['significantPixelChannelSum']
        fraction, mean = changed / pixels, total / (pixels * 3)
        return dict(passed=fraction <= TOLERANCES['changedPixelFraction'] and mean <= TOLERANCES['meanChannelError'],
                    dimensionsMatch=True, changedPixelFraction=fraction, meanChannelError=mean)


def invoke(command, timeout=120, env=None):
    return subprocess.run(command, capture_output=True, text=True, timeout=timeout, env=env, check=True)


def render(pdf, prefix, renderer):
    pattern = re.compile(re.escape(prefix.name) + r'-(\d+)\.png')
    # Remove only this capture's numbered pages; stale pages must not enter metrics.
    for path in prefix.parent.glob(prefix.name + '-*.png'):
        if pattern.fullmatch(path.name):
            path.unlink()
    invoke([renderer, '-r', '96', '-png', str(pdf), str(prefix)])
    return sorted((p for p in prefix.parent.glob(prefix.name + '-*.png') if pattern.fullmatch(p.name)),
                  key=lambda p: int(pattern.fullmatch(p.name)[1]))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, default=ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/Comparison/manifest.json')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/comparison')
    parser.add_argument('--engine', choices=['wordnet', 'aspose', 'both'], default='wordnet')
    parser.add_argument('--aspose-assembly', type=Path)
    parser.add_argument('--aspose-license', type=Path)
    parser.add_argument('--poi-classpath', help='Pinned POI jar directory for independent editable DOC text/reading-order checks')
    parser.add_argument('--editable-reader', default=shutil.which('antiword'))
    parser.add_argument('--references', type=Path, help='Caller-owned reference manifest; see docs/comparison.md')
    parser.add_argument('--capture-aspose-reference', type=Path)
    parser.add_argument('--iterations', type=int, default=5)
    parser.add_argument('--dotnet', default=shutil.which('dotnet'))
    parser.add_argument('--renderer', default=shutil.which('pdftoppm'))
    parser.add_argument('--worker', type=Path, default=ROOT / 'tools/wordnet.Benchmarks/bin/Release/net10.0/wordnet.Benchmarks.dll')
    args = parser.parse_args()
    if not args.dotnet or not args.renderer or not args.worker.is_file():
        parser.error('Build the benchmark project and install dotnet and Poppler pdftoppm; pass explicit paths if necessary.')
    if not 1 <= args.iterations <= 100:
        parser.error('--iterations must be 1..100')
    if args.engine != 'wordnet' and (not args.aspose_assembly or not args.aspose_assembly.is_file()):
        parser.error('Aspose requires a caller-provisioned legitimate Aspose.Words.dll and dependencies.')
    if args.aspose_license and (not args.aspose_license.is_file() or args.engine == 'wordnet'):
        parser.error('A license file requires an Aspose run and must exist.')
    import PIL
    if PIL.__version__ != '12.3.0':
        parser.error('Use the pinned test-only Pillow==12.3.0 from eng/comparison-requirements.txt')
    manifest = json.loads(args.manifest.read_text())
    if manifest['schemaVersion'] != 1:
        parser.error('Unsupported corpus schema')
    args.output.mkdir(parents=True, exist_ok=True)
    font_dir = args.output / 'fonts'
    font_dir.mkdir(exist_ok=True)
    # Require a clean font set so two providers cannot silently use different assets.
    expected_font_names = {Path(f['path']).name for f in manifest['fonts']}
    if any(p.name not in expected_font_names for p in font_dir.iterdir()):
        parser.error('Output fonts directory contains unexpected files; choose a fresh output directory.')
    for font in manifest['fonts']:
        path = ROOT / font['path']
        if digest(path) != font['sha256']:
            parser.error('Pinned font hash mismatch: ' + font['path'])
        shutil.copyfile(path, font_dir / path.name)
    renderer_process = subprocess.run([args.renderer, '-v'], capture_output=True, text=True, check=True)
    renderer_version = renderer_identity(renderer_process.stderr or renderer_process.stdout)
    references = [(r, args.references.parent) for r in json.loads(args.references.read_text(encoding='utf-8-sig'))['references']] if args.references else []
    references.extend((r, args.manifest.parent) for d in manifest['documents'] for r in d.get('references', []))
    report = dict(schemaVersion=1, manifestSha256=digest(args.manifest), sourceRevision=invoke(['git', '-C', str(ROOT), 'rev-parse', 'HEAD']).stdout.strip(),
                  sourceDirty=bool(invoke(['git', '-C', str(ROOT), 'status', '--porcelain']).stdout.strip()),
                  python=platform.python_version(), pillow=PIL.__version__, renderer=renderer_version, rendererSha256=digest(args.renderer),
                  tolerances=TOLERANCES, fonts=manifest['fonts'], results=[])
    env = os.environ.copy()
    if args.aspose_license:
        env['WORDNET_ASPOSE_LICENSE'] = str(args.aspose_license.resolve())
    else:
        env.pop('WORDNET_ASPOSE_LICENSE', None)
    captured = []
    for document in manifest['documents']:
        source = args.manifest.parent / document['path']
        if digest(source) != document['sha256']:
            parser.error('Input hash mismatch: ' + document['id'])
        for engine in (['wordnet', 'aspose'] if args.engine == 'both' else [args.engine]):
            prefix = args.output / (document['id'] + '-' + engine)
            pdf, worker_report = prefix.with_suffix('.pdf'), prefix.with_suffix('.json')
            result = dict(documentId=document['id'], category=document['category'], engine=engine, inputSha256=document['sha256'], knownLosses=document['knownLosses'])
            report['results'].append(result)
            try:
                started = time.perf_counter()
                invoke([args.dotnet, str(args.worker), '--comparison', engine, str(source), str(pdf), str(worker_report), str(font_dir), str(args.iterations),
                        str(args.aspose_assembly.resolve()) if args.aspose_assembly else '-'], env=env)
                result['workerWallMilliseconds'] = (time.perf_counter() - started) * 1000
                measured = json.loads(worker_report.read_text())
                result['measurement'] = measured
                result['status'] = 'measured'
                if engine == 'wordnet':
                    observed = {d['code'] for d in measured['diagnostics']}
                    result['observedKnownLosses'] = sorted(observed.intersection(document['knownLosses']))
                    result['knownLossChecks'] = loss_check(document['knownLosses'], measured['diagnostics'])
                    result['fidelityStatus'] = 'detected-losses' if any(d['severity'] == 'Warning' for d in measured['diagnostics']) else 'no-detected-losses'
                if engine == 'aspose':
                    captured.append(dict(documentId=document['id'], kind='aspose', version=next(a['version'] for a in measured['assemblies'] if a['name'] == 'Aspose.Words'),
                                         artifactMode=measured['artifactMode'], inputSha256=document['sha256'], pdf=os.path.relpath(pdf.resolve(), args.capture_aspose_reference.parent.resolve()) if args.capture_aspose_reference else pdf.name,
                                         sha256=digest(pdf), fonts=manifest['fonts'], renderer=renderer_version, assemblies=measured['assemblies']))
                if measured['artifactMode'] == 'evaluation':
                    result['authoredChecks'] = dict(status='excluded-evaluation-artifact')
                    result['referenceChecks'] = [dict(status='excluded-evaluation-artifact')]
                    continue
                result['authoredChecks'] = dict(basis='authored-source-expectations', **content_check(measured['snapshot'], document['requiredText']),
                                               pageCountMatches=measured['snapshot']['pageCount'] == document['expectedPages'])
                if args.poi_classpath:
                    jars = sorted(Path(args.poi_classpath).glob('*.jar'))
                    if not jars:
                        raise ValueError('No POI jars in --poi-classpath')
                    editable = invoke(['java', '-Dlog4j2.loggerContextFactory=org.apache.logging.log4j.simple.SimpleLoggerContextFactory', '-cp', str(Path(args.poi_classpath) / '*'), str(ROOT / 'eng/DocInteropCheck.java'), '--extract-text', str(prefix.with_suffix('.doc'))]).stdout
                    result['editableContent'] = dict(reader='Apache POI HWPF', assemblies=[dict(name=p.name, sha256=digest(p)) for p in jars],
                                                     **content_check(dict(pages=[dict(text=editable)]), document['requiredText']))
                elif args.editable_reader:
                    editable = invoke([args.editable_reader, '-t', str(prefix.with_suffix('.doc'))]).stdout
                    result['editableContent'] = dict(readerVersion=subprocess.run([args.editable_reader, '-h'], capture_output=True, text=True).stderr.strip(),
                                                     readerSha256=digest(args.editable_reader), **content_check(dict(pages=[dict(text=editable)]), document['requiredText']))
                else:
                    result['editableContent'] = dict(status='independent-reader-not-provided')
                images = render(pdf, prefix, args.renderer)
                result['rasters'] = [dict(path=p.name, sha256=digest(p)) for p in images]
                result['referenceChecks'] = []
                for reference, reference_root in [(r, base) for r, base in references if r['documentId'] == document['id']]:
                    check = dict(kind=reference['kind'], version=reference['version'])
                    result['referenceChecks'].append(check)
                    if reference['artifactMode'] != 'licensed':
                        check['status'] = 'excluded-evaluation-artifact'
                        continue
                    if reference['inputSha256'] != document['sha256'] or reference['fonts'] != manifest['fonts']:
                        check['status'] = 'incomparable-input-or-fonts'
                        continue
                    reference_pdf = (reference_root / reference['pdf']).resolve()
                    if digest(reference_pdf) != reference['sha256']:
                        raise ValueError('Reference PDF hash mismatch')
                    snapshot_path = prefix.with_name(prefix.name + '-reference.json')
                    invoke([args.dotnet, str(args.worker), '--comparison', 'snapshot', str(reference_pdf), str(snapshot_path)])
                    check.update(snapshot_check(measured['snapshot'], json.loads(snapshot_path.read_text())))
                    if renderer_identity(reference['renderer']) != renderer_version:
                        check['visualStatus'] = 'incomparable-renderer-version'
                    else:
                        reference_images = render(reference_pdf, prefix.with_name(prefix.name + '-reference'), args.renderer)
                        check['visual'] = [pixel_check(a, b) for a, b in zip(images, reference_images)]
                    check['status'] = 'compared'
                if not result['referenceChecks']:
                    result['referenceChecks'].append(dict(status='not-provided'))
            except (OSError, ValueError, KeyError, subprocess.SubprocessError) as error:
                # A failed conversion stays in the report; do not drop difficult documents.
                result.update(status='failed', error=type(error).__name__, detail=str(error))
            finally:
                (args.output / 'results.json').write_text(json.dumps(report, indent=2) + '\n')
    if args.capture_aspose_reference:
        args.capture_aspose_reference.write_text(json.dumps(dict(schemaVersion=1, references=captured), indent=2) + '\n')
    for result in report['results']:
        for check in result.get('referenceChecks', []):
            if check['status'] == 'compared':
                check['textAndGeometryPassed'] = all(check.get(k, False) for k in ('pageCountMatches', 'pageGeometryMatches', 'textAndOrderMatches', 'glyphPlacementPasses'))
                check['visualPassed'] = all(p['passed'] for p in check['visual']) if 'visual' in check else None
    (args.output / 'results.json').write_text(json.dumps(report, indent=2) + '\n')
    failures = [r for r in report['results'] if r['status'] == 'failed' or r.get('knownLossChecks', {}).get('passed') is False or r.get('authoredChecks', {}).get('passed') is False or r.get('authoredChecks', {}).get('pageCountMatches') is False or r.get('editableContent', {}).get('passed') is False or any(c.get('textAndGeometryPassed') is False or c.get('visualPassed') is False for c in r.get('referenceChecks', []))]
    print(f"{len(report['results'])} results; {len(failures)} failures retained in {args.output / 'results.json'}")
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
