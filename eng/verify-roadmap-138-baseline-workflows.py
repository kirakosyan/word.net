#!/usr/bin/env python3
"""Independently check release-source and public-package workflow observations."""
import argparse
import copy
import hashlib
import json
import subprocess
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
XHTML = '{http://www.w3.org/1999/xhtml}'
EXPECTED = {'fragment': 'Independent fragment text.', 'accepted': 'Before new After', 'rejected': 'Before old After'}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def docx_text(path):
    with ZipFile(path) as package:
        root = ET.fromstring(package.read('word/document.xml'))
    if list(root.iter(W + 'ins')) or list(root.iter(W + 'del')):
        raise ValueError('Resolved output retained revision markup: ' + path.name)
    return ''.join(item.text or '' for item in root.iter(W + 't'))


def bbox(path):
    root = ET.parse(path).getroot()
    words = [(item.text, *(item.get(key) for key in ('xMin', 'yMin', 'xMax', 'yMax')))
             for item in root.iter(XHTML + 'word')]
    if not words:
        raise ValueError('Independent PDF word coordinates are missing: ' + path.name)
    return words


def collect(fixtures, source, package, independent, source_binary, public_archive, input_log, output_log):
    manifest = json.loads((fixtures / 'manifest.json').read_text())
    with ZipFile(public_archive) as archive:
        public_binary = sha(archive.read('lib/net10.0/wordnet.dll'))
    data = {'manifest': manifest, 'sourceBinary': sha(source_binary.read_bytes()), 'publicBinary': public_binary,
            'publicArchive': sha(public_archive.read_bytes()), 'modes': {},
            'inputSchema': input_log.read_text(), 'outputSchema': output_log.read_text()}
    for mode, directory in (('source', source), ('package', package)):
        report = json.loads((directory / 'report.json').read_text())
        results = {}
        for name in EXPECTED:
            docx, pdf = directory / (name + '.docx'), directory / (name + '.pdf')
            stem = independent / (mode + '-' + name)
            info_json = independent / (mode + '-' + name + '-info.json')
            info = (json.loads(info_json.read_text())['raw'] if info_json.exists()
                    else (independent / (mode + '-' + name + '-info.txt')).read_text())
            results[name] = {'docxHash': sha(docx.read_bytes()), 'pdfHash': sha(pdf.read_bytes()),
                             'docxText': docx_text(docx),
                             'pdfText': ' '.join((independent / (mode + '-' + name + '.txt')).read_text().split()),
                             'bbox': bbox(independent / (mode + '-' + name + '-bbox.html')),
                             'pngHash': sha(stem.with_suffix('.png').read_bytes()),
                             'onePage': 'Pages:           1' in info}
        data['modes'][mode] = {'report': report, 'results': results}
    data['inputs'] = {path.name: sha(path.read_bytes()) for path in fixtures.glob('*.docx')}
    return data


def check(data):
    if data['manifest']['documents'] != data['inputs'] or len(data['inputs']) != 4:
        raise ValueError('Input fixture identities changed')
    for name, digest in data['inputs'].items():
        if name + ' ' + digest + ' errors=0' not in data['inputSchema']:
            raise ValueError('Input schema evidence missing: ' + name)
    if data['publicArchive'] != '5a7c20bb5b4271482d8756c44f756542365fddfb519151444398b071b0e0c1b9':
        raise ValueError('Public archive identity changed')
    for mode, expected_binary in (('source', data['sourceBinary']), ('package', data['publicBinary'])):
        item = data['modes'][mode]
        report, outputs = item['report'], item['results']
        if report['inputHashes'] != data['inputs'] or report['assemblies']['wordnet']['sha256'] != expected_binary:
            raise ValueError('Loaded input or core identity changed: ' + mode)
        fragment, review = report['fragment'], report['review']
        if (fragment['fragmentException'], fragment['fragmentCode'], fragment['hostUnchanged'], fragment['fragmentUnchanged']) != (
                'NotSupportedException', 'Fragment.Structure', True, True):
            raise ValueError('Field-bearing fragment boundary or atomicity changed: ' + mode)
        if (review['reviewException'], review['reviewCode'], review['outputUntouched'], review['originalUnchanged'], review['retainedRevisions']) != (
                'NotSupportedException', 'Review.ExplicitViewRequired', True, True, 2):
            raise ValueError('Retained-history review boundary or atomicity changed: ' + mode)
        for name, expected in EXPECTED.items():
            output = outputs[name]
            reported_docx = fragment['outputDocxSha256'] if name == 'fragment' else review[name + 'DocxSha256']
            reported_pdf = fragment['outputPdfSha256'] if name == 'fragment' else review[name + 'PdfSha256']
            if (output['docxHash'], output['pdfHash']) != (reported_docx, reported_pdf):
                raise ValueError('Output hashes do not match the loaded worker: ' + mode + '/' + name)
            if name + '.docx ' + output['docxHash'] + ' errors=0' not in data['outputSchema']:
                raise ValueError('Output schema evidence missing: ' + mode + '/' + name)
            if output['docxText'] != expected or output['pdfText'] != expected or not output['onePage']:
                raise ValueError('Output text, order or page count changed: ' + mode + '/' + name)
    for name in EXPECTED:
        a, b = (data['modes'][mode]['results'][name] for mode in ('source', 'package'))
        if any(a[key] != b[key] for key in ('docxHash', 'docxText', 'pdfText', 'bbox', 'pngHash', 'onePage')):
            raise ValueError('Source/package output observations differ: ' + name)
    return {'fixtures': 4, 'outputsPerMode': 3, 'inputAndOutputSchemaValid': True,
            'fieldFragmentRejectedAtomically': True, 'retainedHistoryPdfRejectedAtomically': True,
            'explicitReviewViewsAndPlainFragmentMatch': True,
            'sourceAndPublicTextGeometryAndPixelsMatch': True,
            'limitations': 'These controls do not implement a redline display or measure licensed-reference fidelity.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('fixtures', 'source', 'package', 'independent', 'source_binary', 'public_archive', 'input_log', 'output_log'):
        parser.add_argument(name, type=Path)
    parser.add_argument('--release-checkout', required=True, type=Path)
    args = parser.parse_args()
    commit = subprocess.check_output(['git', '-C', str(args.release_checkout), 'rev-parse', 'HEAD'], text=True).strip()
    status = subprocess.check_output(['git', '-C', str(args.release_checkout), 'status', '--porcelain'], text=True)
    if commit != 'bf5ab004d1622e66084aedf4bbc83e3cf8d9c2c7' or status:
        raise ValueError('Release-source checkout identity or cleanliness changed')
    data = collect(args.fixtures, args.source, args.package, args.independent, args.source_binary,
                   args.public_archive, args.input_log, args.output_log)
    result = check(data)
    controls = {}
    names = ('input changed', 'source core changed', 'public archive changed', 'package core changed',
             'fragment admitted', 'fragment host mutated', 'fragment source mutated', 'review admitted',
             'review output mutated', 'review history mutated', 'accepted text changed', 'rejected text changed',
             'output hash changed', 'glyph position changed', 'raster changed', 'schema evidence missing')
    for name in names:
        changed = copy.deepcopy(data)
        source = changed['modes']['source']; package = changed['modes']['package']
        if name == 'input changed': changed['inputs']['review.docx'] = '0' * 64
        elif name == 'source core changed': changed['sourceBinary'] = '0' * 64
        elif name == 'public archive changed': changed['publicArchive'] = '0' * 64
        elif name == 'package core changed': package['report']['assemblies']['wordnet']['sha256'] = '0' * 64
        elif name == 'fragment admitted': source['report']['fragment']['fragmentCode'] = 'none'
        elif name == 'fragment host mutated': source['report']['fragment']['hostUnchanged'] = False
        elif name == 'fragment source mutated': source['report']['fragment']['fragmentUnchanged'] = False
        elif name == 'review admitted': source['report']['review']['reviewCode'] = 'none'
        elif name == 'review output mutated': source['report']['review']['outputUntouched'] = False
        elif name == 'review history mutated': source['report']['review']['originalUnchanged'] = False
        elif name == 'accepted text changed': source['results']['accepted']['pdfText'] = 'Before old After'
        elif name == 'rejected text changed': source['results']['rejected']['docxText'] = 'Before new After'
        elif name == 'output hash changed': source['results']['fragment']['pdfHash'] = '0' * 64
        elif name == 'glyph position changed': package['results']['fragment']['bbox'][0] = ('wrong',)
        elif name == 'raster changed': package['results']['fragment']['pngHash'] = '0' * 64
        elif name == 'schema evidence missing': changed['inputSchema'] = ''
        try:
            check(changed)
        except (ValueError, KeyError, TypeError, IndexError) as error:
            controls[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'result': result, 'negativeControls': controls,
                      'controlMethod': 'Observation records were altered in memory and checked against independent XML, PDF, schema and identities.'}, indent=2))


if __name__ == '__main__':
    main()
