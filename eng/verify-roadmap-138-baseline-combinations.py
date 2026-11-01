#!/usr/bin/env python3
"""Verify retained release-source and public-package factorial comparison evidence."""
import argparse
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
from zipfile import ZipFile

from PIL import Image, ImageChops, __version__ as PILLOW_VERSION

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('comparison', ROOT / 'eng/run-comparison.py')
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)

CASES = ('explicit-direct', 'explicit-styled', 'theme-direct', 'theme-styled')
EXPECTED = {'explicit-direct': [], 'explicit-styled': ['Docx.TableFormatting'],
            'theme-direct': ['Docx.ThemeFont'], 'theme-styled': ['Docx.TableFormatting', 'Docx.ThemeFont']}
RELEASE = 'bf5ab004d1622e66084aedf4bbc83e3cf8d9c2c7'
PUBLIC_ARCHIVE_SHA256 = '5a7c20bb5b4271482d8756c44f756542365fddfb519151444398b071b0e0c1b9'


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def check(fixtures, office, directories, reports, defects, expected_libraries, public_archive_sha, image_overrides=None):
    image_overrides = image_overrides or {}
    if public_archive_sha != PUBLIC_ARCHIVE_SHA256:
        raise ValueError('Public core archive identity changed')

    def image(mode, name, reference=False):
        key = (mode, name, reference)
        if key in image_overrides:
            return image_overrides[key]
        suffix = '-wordnet-reference-1.png' if reference else '-wordnet-1.png'
        return Image.open(directories[mode] / (name + suffix)).convert('RGB')

    manifest = json.loads((fixtures / 'manifest.json').read_text())
    if {case['id'] for case in manifest['documents']} != set(CASES):
        raise ValueError('Unexpected authored factorial inputs')
    for case in manifest['documents']:
        if digest(fixtures / case['path']) != case['sha256']:
            raise ValueError('Input fixture changed: ' + case['id'])
    references = json.loads((office / 'references.json').read_text())['references']
    if {item['documentId'] for item in references} != set(CASES):
        raise ValueError('Reference inputs are incomplete')
    for item in references:
        if item['kind'] != 'libreoffice' or item['version'] != 'LibreOffice 24.2.7.2 420(Build:2)' or item['artifactMode'] != 'open-source':
            raise ValueError('Reference producer changed')
        if digest(office / item['pdf']) != item['sha256']:
            raise ValueError('Reference PDF changed: ' + item['documentId'])
    all_results = {}
    for mode in ('source', 'package'):
        report = reports[mode]
        comparison.validate_defect_report(report, defects[mode])
        if report['sourceRevision'] != RELEASE or report['sourceDirty'] or report['tolerances'] != comparison.TOLERANCES:
            raise ValueError('Release source or comparison tolerances changed: ' + mode)
        if report['pillow'] != '12.3.0' or not report['renderer'].startswith('pdftoppm version 24.02.0\n'):
            raise ValueError('Comparison reader identity changed: ' + mode)
        results = {result['documentId']: result for result in report['results']}
        if len(report['results']) != 4 or set(results) != set(CASES):
            raise ValueError('Missing or duplicate results: ' + mode)
        for name, result in results.items():
            loaded = {item['name'] + '.dll': item['sha256'] for item in result['measurement']['assemblies']}
            if loaded.get('wordnet.dll') != expected_libraries[mode]:
                raise ValueError('Unexpected loaded core library: ' + mode)
            loss = result['knownLossChecks']
            if result['status'] != 'measured' or loss['missing'] or sorted(loss['unexpected']) != EXPECTED[name] or loss['passed'] != (not EXPECTED[name]):
                raise ValueError('Loss verdict changed: ' + mode + '/' + name)
            if not result['authoredChecks']['passed'] or not result['authoredChecks']['pageCountMatches'] or not result['editableContent']['passed']:
                raise ValueError('Authored or independently read content changed: ' + mode + '/' + name)
            reference = result['referenceChecks'][0]
            if reference['status'] != 'compared' or not reference['pageCountMatches'] or not reference['textAndOrderMatches']:
                raise ValueError('Reference page or text changed: ' + mode + '/' + name)
            if reference['pageGeometryMatches'] or reference['glyphPlacementPasses'] != name.startswith('explicit-'):
                raise ValueError('Geometry or glyph verdict changed: ' + mode + '/' + name)
            if reference['visualPassed'] != (name == 'explicit-direct'):
                raise ValueError('Raster verdict changed: ' + mode + '/' + name)
            if not comparison.failure_verdicts(result):
                raise ValueError('A reference failure was discarded: ' + mode + '/' + name)
        all_results[mode] = results
    for name in CASES:
        source, package = all_results['source'][name], all_results['package'][name]
        for key in ('authoredChecks', 'editableContent', 'knownLossChecks', 'referenceChecks'):
            if source[key] != package[key]:
                raise ValueError('Source/package verdicts differ: ' + name + '/' + key)
        if source['measurement']['snapshot'] != package['measurement']['snapshot']:
            raise ValueError('Source/package glyph snapshots differ: ' + name)
        source_image, package_image = image('source', name), image('package', name)
        if ImageChops.difference(source_image, package_image).getbbox() is not None:
            raise ValueError('Source/package rendered pixels differ: ' + name)
        colors = source_image.getcolors(source_image.width * source_image.height)
        blue = sum(count for count, color in colors if color == (34, 102, 204)) if colors else 0
        if (name.endswith('-styled') and blue != 0) or (name.endswith('-direct') and blue == 0):
            raise ValueError('Release blue-shading contrast changed: ' + name)
    reference_images = [image('source', name, reference=True) for name in CASES]
    if any(ImageChops.difference(reference_images[0], image).getbbox() is not None for image in reference_images[1:]):
        raise ValueError('The independent factorial controls do not have identical reference pixels')
    colors = reference_images[0].getcolors(reference_images[0].width * reference_images[0].height)
    if not colors or not any(count > 0 and color == (34, 102, 204) for count, color in colors):
        raise ValueError('The independent reference lacks the authored blue table shading')
    for name in CASES:
        reference = json.loads((directories['source'] / (name + '-wordnet-reference.json')).read_text())['pages'][0]
        actual = all_results['source'][name]['measurement']['snapshot']['pages'][0]
        if abs(actual['width'] - 595.3) > 0.00001 or abs(actual['height'] - 841.9) > 0.00001 \
                or abs(reference['width'] - 595.303937007874) > 0.00001 or abs(reference['height'] - 841.889763779528) > 0.00001:
            raise ValueError('Page-size rounding observation changed: ' + name)
    return {'documents': 4, 'sourceAndPublicVerdictsEqual': True, 'sourceAndPublicSnapshotsEqual': True,
            'independentReferencePixelsEqual': True, 'allReferenceFailuresRetainedPerMode': 4,
            'combinedWarningCodes': EXPECTED['theme-styled'], 'pageGeometryRoundingObserved': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('fixtures', type=Path)
    parser.add_argument('office', type=Path)
    parser.add_argument('source', type=Path)
    parser.add_argument('package', type=Path)
    parser.add_argument('source_library', type=Path)
    parser.add_argument('public_archive', type=Path)
    args = parser.parse_args()
    if PILLOW_VERSION != '12.3.0':
        parser.error('Use the pinned Pillow 12.3.0 for these reference comparisons.')
    directories = {'source': args.source, 'package': args.package}
    reports = {mode: json.loads((path / 'results.json').read_text()) for mode, path in directories.items()}
    defects = {mode: json.loads((path / 'defects.json').read_text()) for mode, path in directories.items()}
    with ZipFile(args.public_archive) as archive:
        public_sha = hashlib.sha256(archive.read('lib/net10.0/wordnet.dll')).hexdigest()
    libraries = {'source': digest(args.source_library), 'package': public_sha}
    public_archive_sha = digest(args.public_archive)
    result = check(args.fixtures, args.office, directories, reports, defects, libraries, public_archive_sha)
    controls = {}
    names = ('source loss hidden', 'package loss hidden', 'combination loss hidden', 'raster failure hidden',
             'glyph failure hidden', 'geometry failure hidden', 'content loss invented', 'source library changed',
             'package library changed', 'release identity changed', 'dirty source claimed', 'case omitted',
             'threshold changed', 'reference comparison omitted', 'source/package snapshot changed',
             'styled blue pixels invented', 'reference pixels changed', 'public archive changed')
    for name in names:
        altered, changed_libraries = copy.deepcopy(reports), dict(libraries)
        changed_images = {}
        changed_archive_sha = public_archive_sha
        source = {x['documentId']: x for x in altered['source']['results']}
        package = {x['documentId']: x for x in altered['package']['results']}
        if name == 'source loss hidden': source['theme-direct']['knownLossChecks']['unexpected'] = []
        elif name == 'package loss hidden': package['explicit-styled']['knownLossChecks']['unexpected'] = []
        elif name == 'combination loss hidden': source['theme-styled']['knownLossChecks']['unexpected'].pop()
        elif name == 'raster failure hidden': source['theme-styled']['referenceChecks'][0]['visualPassed'] = True
        elif name == 'glyph failure hidden': source['theme-direct']['referenceChecks'][0]['glyphPlacementPasses'] = True
        elif name == 'geometry failure hidden': source['explicit-direct']['referenceChecks'][0]['pageGeometryMatches'] = True
        elif name == 'content loss invented': source['explicit-direct']['authoredChecks']['passed'] = False
        elif name == 'source library changed': changed_libraries['source'] = '0' * 64
        elif name == 'package library changed': changed_libraries['package'] = '0' * 64
        elif name == 'release identity changed': altered['source']['sourceRevision'] = '0' * 40
        elif name == 'dirty source claimed': altered['source']['sourceDirty'] = True
        elif name == 'case omitted': altered['package']['results'].pop()
        elif name == 'threshold changed': altered['source']['tolerances']['glyphPoints'] = 1
        elif name == 'reference comparison omitted': source['theme-styled']['referenceChecks'].clear()
        elif name == 'source/package snapshot changed': package['theme-styled']['measurement']['snapshot']['pages'][0]['width'] = 1
        elif name == 'styled blue pixels invented':
            for mode in ('source', 'package'):
                changed = Image.open(directories[mode] / 'theme-styled-wordnet-1.png').convert('RGB')
                changed.putpixel((0, 0), (34, 102, 204))
                changed_images[(mode, 'theme-styled', False)] = changed
        elif name == 'reference pixels changed':
            changed = Image.open(directories['source'] / 'explicit-direct-wordnet-reference-1.png').convert('RGB')
            changed.putpixel((0, 0), (0, 0, 0))
            changed_images[('source', 'explicit-direct', True)] = changed
        elif name == 'public archive changed': changed_archive_sha = '0' * 64
        try:
            check(args.fixtures, args.office, directories, altered, defects, changed_libraries, changed_archive_sha, changed_images)
        except (ValueError, KeyError, TypeError, IndexError, AssertionError) as error:
            controls[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'result': result, 'negativeControls': controls,
                      'controlMethod': 'Retained comparison results and loaded-binary identities changed in memory.'}, indent=2))


if __name__ == '__main__':
    main()
