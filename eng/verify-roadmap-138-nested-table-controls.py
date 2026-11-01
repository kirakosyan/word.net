#!/usr/bin/env python3
"""Verify retained release nested-table contrasts and independently observed PDF geometry."""
import argparse
import copy
import hashlib
import io
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile

from PIL import Image, ImageChops

ROOT = Path(__file__).resolve().parents[1]
W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
NAMES = ('wide-no-trailing-p', 'wide-trailing-p', 'fit-no-trailing-p', 'fit-trailing-p')
SOURCE = 'bf5ab004d1622e66084aedf4bbc83e3cf8d9c2c7'
BASE = '7889299fb8b6ee11c16ac7d0af9591411740b96e13b7f7783d29a0261c971360'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def bounds(path):
    result = []
    for word in ET.parse(path).getroot().findall('.//{*}word'):
        result.append({'text': word.text or '', 'x': float(word.attrib['xMin']), 'y': float(word.attrib['yMin'])})
    return result


def structure(path):
    with ZipFile(path) as archive:
        document = ET.fromstring(archive.read('word/document.xml'))
    outer = document.find(W + 'body/' + W + 'tbl')
    cells = outer.find(W + 'tr').findall(W + 'tc')
    inner = cells[0].find(W + 'tbl')
    return {'outerWidth': int(outer.find(W + 'tblPr/' + W + 'tblW').attrib[W + 'w']),
            'nestedWidth': int(inner.find(W + 'tblPr/' + W + 'tblW').attrib[W + 'w']),
            'nestedGrid': [int(item.attrib[W + 'w']) for item in inner.find(W + 'tblGrid').findall(W + 'gridCol')],
            'terminalParagraph': cells[0][-1].tag == W + 'p',
            'text': ''.join(item.text or '' for item in document.iter(W + 't'))}


def pixels(actual, reference):
    with Image.open(io.BytesIO(actual)) as a, Image.open(io.BytesIO(reference)) as b:
        if a.size != b.size:
            return {'passed': False, 'dimensionsMatch': False}
        difference = ImageChops.difference(a.convert('RGB'), b.convert('RGB'))
        total = changed = 0
        for r, g, blue in difference.get_flattened_data():
            delta = r + g + blue; total += delta; changed += delta > 96
        count = a.width * a.height; fraction = changed / count; mean = total / (count * 3)
        return {'passed': fraction <= .002 and mean <= .25, 'dimensionsMatch': True,
                'changedPixelFraction': fraction, 'meanChannelError': mean}


def collect(fixtures, office, source, package, poppler, schema_log, source_worker, package_worker, source_core, public_archive):
    manifest = load(fixtures / 'manifest.json')
    reports = {mode: load(directory / 'results.json') for mode, directory in (('source', source), ('package', package))}
    cases = {}
    for name in NAMES:
        cases[name] = {
            'inputSha256': sha((fixtures / (name + '.docx')).read_bytes()), 'structure': structure(fixtures / (name + '.docx')),
            'referenceSha256': sha((office / (name + '.pdf')).read_bytes()),
            'referenceSnapshots': {mode: load(directory / (name + '-wordnet-reference.json')) for mode, directory in (('source', source), ('package', package))},
            'outputHashes': {mode: {'pdf': sha((directory / (name + '-wordnet.pdf')).read_bytes()),
                                    'doc': sha((directory / (name + '-wordnet.doc')).read_bytes())}
                             for mode, directory in (('source', source), ('package', package))},
            'renders': {mode: {'actual': (directory / (name + '-wordnet-1.png')).read_bytes(),
                               'reference': (directory / (name + '-wordnet-reference-1.png')).read_bytes()}
                        for mode, directory in (('source', source), ('package', package))},
            'bounds': {mode: bounds(poppler / (mode + '-' + name + '-bbox.html')) for mode in ('source', 'package', 'office')},
            'pageInfo': {mode: (poppler / (mode + '-' + name + '-info.txt')).read_text() for mode in ('source', 'package', 'office')}}
    history = load(ROOT / 'docs/measurements/roadmap-138-combination-baseline/receipt.json')
    reference_tool = load(ROOT / 'docs/measurements/roadmap-138-combination-baseline/office/references.json')['references'][0]
    original = {}
    for mode in ('source', 'package'):
        report = load(ROOT / ('docs/measurements/roadmap-138-initial/existing-24-' + mode + '/results.json'))
        original[mode] = next(item for item in report['results'] if item['documentId'] == 'invoice-nested')
    with ZipFile(public_archive) as package_zip:
        public_core = sha(package_zip.read('lib/net10.0/wordnet.dll'))
    return {'manifest': manifest, 'reports': reports, 'cases': cases, 'schemaLog': schema_log.read_text(),
            'references': load(office / 'references.json'), 'history': history, 'referenceTool': reference_tool, 'original': original,
            'generatorSha256': sha((ROOT / 'eng/generate-roadmap-138-nested-table-controls.py').read_bytes()),
            'helperSha256': sha((ROOT / 'eng/generate-fidelity-reductions.py').read_bytes()),
            'sourceWorker': sha(source_worker.read_bytes()), 'packageWorker': sha(package_worker.read_bytes()),
            'sourceCore': sha(source_core.read_bytes()), 'publicArchive': sha(public_archive.read_bytes()), 'publicCore': public_core}


def near(value, expected, tolerance=.05):
    return abs(value - expected) <= tolerance


def check(data):
    manifest, cases, history = data['manifest'], data['cases'], data['history']
    if manifest['baseSha256'] != BASE or set(cases) != set(NAMES): raise ValueError('Baseline or case set changed')
    if manifest['generatorSha256'] != data['generatorSha256'] or manifest['helperSha256'] != data['helperSha256']:
        raise ValueError('Fixture generator identity changed')
    if data['sourceWorker'] != history['sourceWorkerSha256'] or data['packageWorker'] != history['packageWorkerSha256']:
        raise ValueError('Release worker identity changed')
    if data['sourceCore'] != history['sourceCoreSha256'] or data['publicCore'] != history['packageCoreSha256']:
        raise ValueError('Release or public-package core identity changed')
    if data['publicArchive'] != '5a7c20bb5b4271482d8756c44f756542365fddfb519151444398b071b0e0c1b9':
        raise ValueError('Public package archive identity changed')
    docs = {item['id']: item for item in manifest['documents']}
    refs = {item['documentId']: item for item in data['references']['references']}
    if set(docs) != set(NAMES) or set(refs) != set(NAMES): raise ValueError('Manifest or reference set changed')
    results = {}
    for mode in ('source', 'package'):
        report = data['reports'][mode]
        if report['sourceRevision'] != SOURCE or report['sourceDirty'] or report['tolerances'] != {'glyphPoints': .25, 'significantPixelChannelSum': 96, 'changedPixelFraction': .002, 'meanChannelError': .25}:
            raise ValueError('Release provenance or thresholds changed')
        results[mode] = {item['documentId']: item for item in report['results']}
        if set(results[mode]) != set(NAMES): raise ValueError('Result set changed')
    observations = {}
    for name in NAMES:
        case, doc, ref = cases[name], docs[name], refs[name]
        if doc['sha256'] != case['inputSha256'] or name + '.docx ' + case['inputSha256'] + ' errors=0' not in data['schemaLog']:
            raise ValueError('Input identity or independent schema evidence changed')
        expected_width = 2500 if name.startswith('fit-') else 5000
        if case['structure'] != {'outerWidth': 5000, 'nestedWidth': expected_width,
                                'nestedGrid': [expected_width // 2, expected_width // 2],
                                'terminalParagraph': '-no-trailing-' not in name,
                                'text': 'INVOICE N-202Hardware bundleAdapter80 EURTotal 480 EUR'}:
            raise ValueError('Factorial XML structure changed')
        if ref['sha256'] != case['referenceSha256'] or ref['inputSha256'] != case['inputSha256'] or not ref['version'].startswith('LibreOffice 24.2.7.2') or ref['artifactMode'] != 'open-source':
            raise ValueError('Independent office reference identity changed')
        if any(ref[key] != data['referenceTool'][key] for key in ('version', 'renderer', 'executableSha256', 'binarySha256')) or ref['fonts'] != manifest['fonts']:
            raise ValueError('Independent tool or font identity changed')
        a, b = results['source'][name], results['package'][name]
        for mode, result, core in (('source', a, data['sourceCore']), ('package', b, data['publicCore'])):
            assemblies = {item['name']: item['sha256'] for item in result['measurement']['assemblies']}
            if result['inputSha256'] != case['inputSha256'] or assemblies.get('wordnet') != core:
                raise ValueError('Loaded release assembly or source identity changed')
            if result['measurement']['outputSha256'] != case['outputHashes'][mode]['pdf'] or result['measurement']['editableSha256'] != case['outputHashes'][mode]['doc']:
                raise ValueError('Retained PDF or editable output identity changed')
            if result['status'] != 'measured' or not result['authoredChecks']['passed'] or not result['editableContent']['passed'] or not result['knownLossChecks']['passed']:
                raise ValueError('Authored text, editable output or diagnostic observation changed')
            if result['measurement']['diagnostics']:
                raise ValueError('Unexpected diagnostics appeared')
            observed_pixel = pixels(case['renders'][mode]['actual'], case['renders'][mode]['reference'])
            if observed_pixel != result['referenceChecks'][0]['visual'][0] or observed_pixel['passed']:
                raise ValueError('Retained raster verdict changed')
        if a['measurement']['snapshot'] != b['measurement']['snapshot'] or a['referenceChecks'] != b['referenceChecks']:
            raise ValueError('Release source and public package observations diverged')
        if case['renders']['source']['actual'] != case['renders']['package']['actual'] or case['referenceSnapshots']['source'] != case['referenceSnapshots']['package']:
            raise ValueError('Release source/package raster or reference snapshots diverged')
        if case['bounds']['source'] != case['bounds']['package']:
            raise ValueError('Independent source/package PDF geometry diverged')
        for mode in ('source', 'package', 'office'):
            if 'Pages:           1' not in case['pageInfo'][mode]: raise ValueError('Independent page count changed')
        def word(mode, text):
            matches = [item for item in case['bounds'][mode] if item['text'] == text]
            if len(matches) > 1: raise ValueError('Unexpected duplicate marker')
            return matches[0] if matches else None
        expected_x = 197.1 if '-no-trailing-' not in name else 72.1
        expected_y = 86.992 if '-no-trailing-' not in name else 116.992
        total = word('office', 'Total'); source_total = word('source', 'Total')
        if not total or not source_total or not near(total['x'], expected_x) or not near(total['y'], expected_y) or not near(source_total['x'], 206.603) or not near(source_total['y'], 86.982):
            raise ValueError('Independent outer-cell position changed')
        eighty = word('office', '80')
        if name == 'wide-trailing-p':
            if eighty is not None: raise ValueError('Reference text extraction observation changed')
        elif not eighty or not near(eighty['x'], 140 if name.startswith('fit-') else 202.5):
            raise ValueError('Independent nested-cell position changed')
        check_ref = a['referenceChecks'][0]
        if check_ref['pageGeometryMatches'] or check_ref['visualPassed'] or not check_ref['pageCountMatches']:
            raise ValueError('Retained page or visual verdict changed')
        if name == 'wide-trailing-p':
            if check_ref['textAndOrderMatches'] or check_ref['glyphSequenceMatches']:
                raise ValueError('Reference text/order failure was dropped')
        else:
            maximum = 9.503 if name == 'fit-trailing-p' else 134.503
            if not check_ref['textAndOrderMatches'] or not check_ref['glyphSequenceMatches'] or not near(check_ref['maximumGlyphDisplacementPoints'], maximum, .001):
                raise ValueError('Retained glyph difference changed')
        observations[name] = {'officeTotal': [total['x'], total['y']], 'sourceTotal': [source_total['x'], source_total['y']],
                              'officeEightyPresent': eighty is not None, 'reference': check_ref}
    for mode in ('source', 'package'):
        old, new = data['original'][mode], results[mode]['wide-no-trailing-p']
        if old['measurement']['snapshot'] != new['measurement']['snapshot'] or old['referenceChecks'] != new['referenceChecks']:
            raise ValueError('Original baseline observation did not reproduce')
    return {'cases': observations, 'schemaValidInputs': 4, 'sourcePackageSnapshotsMatch': True,
            'originalBaselineReproduced': True, 'licensedWordReference': 'not-provisioned',
            'interpretation': 'These controlled changes isolate reference sensitivity to a terminal cell paragraph and nested grid width. They do not establish which renderer matches Word or resolve all remaining fidelity differences.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('fixtures', 'office', 'source', 'package', 'poppler', 'schema_log', 'source_worker', 'package_worker', 'source_core', 'public_archive'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    data = collect(args.fixtures, args.office, args.source, args.package, args.poppler, args.schema_log,
                   args.source_worker, args.package_worker, args.source_core, args.public_archive)
    result = check(data)
    mutations = {
        'base identity': lambda d: d['manifest'].update(baseSha256='0' * 64),
        'generator identity': lambda d: d.update(generatorSha256='0' * 64),
        'source worker': lambda d: d.update(sourceWorker='0' * 64),
        'public archive': lambda d: d.update(publicArchive='0' * 64),
        'loaded package': lambda d: d.update(publicCore='0' * 64),
        'schema evidence': lambda d: d.update(schemaLog=''),
        'nested width': lambda d: d['cases']['fit-trailing-p']['structure'].update(nestedWidth=5000),
        'terminal paragraph': lambda d: d['cases']['fit-trailing-p']['structure'].update(terminalParagraph=False),
        'reference identity': lambda d: d['cases']['wide-no-trailing-p'].update(referenceSha256='0' * 64),
        'reference tool identity': lambda d: d['referenceTool'].update(binarySha256='0' * 64),
        'retained PDF identity': lambda d: d['cases']['fit-trailing-p']['outputHashes']['source'].update(pdf='0' * 64),
        'retained editable identity': lambda d: d['cases']['fit-trailing-p']['outputHashes']['package'].update(doc='0' * 64),
        'source identity': lambda d: d['reports']['source']['results'][0].update(inputSha256='0' * 64),
        'authored content': lambda d: d['reports']['source']['results'][0]['authoredChecks'].update(passed=False),
        'missing diagnostic': lambda d: d['reports']['source']['results'][0]['measurement']['diagnostics'].append({'code': 'Unexpected'}),
        'relaxed threshold': lambda d: d['reports']['source']['tolerances'].update(glyphPoints=1),
        'source/package geometry': lambda d: d['cases']['fit-trailing-p']['bounds']['package'][0].update(x=0),
        'reference cell position': lambda d: next(w for w in d['cases']['fit-trailing-p']['bounds']['office'] if w['text'] == 'Total').update(x=0),
        'reference missing text': lambda d: d['cases']['wide-trailing-p']['bounds']['office'].append({'text': '80', 'x': 0, 'y': 0}),
        'page count': lambda d: d['cases']['fit-trailing-p']['pageInfo'].update(office='Pages: 2'),
        'raster evidence': lambda d: d['cases']['fit-trailing-p']['renders']['source'].update(actual=d['cases']['fit-trailing-p']['renders']['source']['reference']),
        'lost fidelity failure': lambda d: d['reports']['source']['results'][1]['referenceChecks'][0].update(textAndOrderMatches=True),
        'original reproduction': lambda d: d['original']['source']['measurement']['snapshot'].update(pageCount=2),
    }
    controls = {}
    for name, mutate in mutations.items():
        altered = copy.deepcopy(data); mutate(altered)
        try:
            check(altered)
        except (ValueError, KeyError, IndexError, TypeError) as error:
            controls[name] = str(error)
        else:
            raise ValueError('Altered observation unexpectedly passed: ' + name)
    print(json.dumps({'result': result, 'negativeControls': controls, 'controlMethod': 'Retained observations were altered in memory.'}, indent=2))


if __name__ == '__main__':
    main()
