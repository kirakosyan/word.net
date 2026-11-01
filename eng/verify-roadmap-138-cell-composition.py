#!/usr/bin/env python3
"""Check created cell-control composition with independent XML and PDF readers."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inspect(path):
    with ZipFile(path) as package:
        root = ET.fromstring(package.read('word/document.xml'))
    body = root.find(W + 'body')
    result = []
    for control in body.iter(W + 'sdt'):
        properties = control.find(W + 'sdtPr'); content = control.find(W + 'sdtContent')
        identifier, tag = properties.find(W + 'id'), properties.find(W + 'tag')
        kinds = [kind for kind in ('text', 'richText') if properties.find(W + kind) is not None]
        if identifier is None or tag is None or len(kinds) != 1:
            raise ValueError('Control identity, tag or type changed')
        result.append({'id': int(identifier.get(W + 'val')), 'tag': tag.get(W + 'val'), 'kind': kinds[0],
                       'value': ''.join(item.text or '' for item in content.iter(W + 't'))})
    tables = body.findall(W + 'tbl')
    if len(tables) != 1:
        raise ValueError('Expected one direct body table: ' + path.name)
    rows = tables[0].findall(W + 'tr')
    if len(rows) != 2 or any(len(row.findall(W + 'tc')) != 3 for row in rows):
        raise ValueError('Table geometry changed: ' + path.name)
    owners = [len(cell.findall(W + 'sdt')) for row in rows for cell in row.findall(W + 'tc')]
    text = ''.join(item.text or '' for item in body.iter(W + 't'))
    return {'controls': result, 'cellWrapperCounts': owners, 'text': text}


def collect(fixtures, observed, office, assembly, input_log, output_log):
    return {'manifest': json.loads((fixtures / 'manifest.json').read_text()),
            'inputs': {path.name: sha(path) for path in fixtures.glob('*.docx')},
            'report': json.loads((observed / 'report.json').read_text()), 'assembly': sha(assembly),
            'source': inspect(observed / 'source.docx'), 'composed': inspect(observed / 'composed.docx'),
            'sourceHash': sha(observed / 'source.docx'), 'docxHash': sha(observed / 'composed.docx'),
            'pdfHash': sha(observed / 'composed.pdf'),
            'officeText': (office / 'office.txt').read_text().split(), 'wordnetText': (office / 'wordnet.txt').read_text().split(),
            'inputSchema': input_log.read_text(), 'outputSchema': output_log.read_text()}


def check(data):
    report = data['report']
    if len(data['inputs']) != 3 or data['manifest']['documents'] != data['inputs'] or report['inputHashes'] != data['inputs']:
        raise ValueError('Input identities changed')
    if report['assemblySha256'] != data['assembly']:
        raise ValueError('Loaded core identity changed')
    for name, digest in data['inputs'].items():
        if name + ' ' + digest + ' errors=0' not in data['inputSchema']:
            raise ValueError('Input schema evidence missing: ' + name)
    for name, digest in (('source.docx', data['sourceHash']), ('composed.docx', data['docxHash'])):
        if name + ' ' + digest + ' errors=0' not in data['outputSchema']:
            raise ValueError('Output schema evidence missing: ' + name)
    if (report['outputs']['source'], report['outputs']['docx'], report['outputs']['pdf']) != (data['sourceHash'], data['docxHash'], data['pdfHash']):
        raise ValueError('Output identities changed')
    if report['sourceUnchanged'] is not True or report['rejectedTargetUnchanged'] is not True:
        raise ValueError('Source or rejected target changed')
    if (report['exception'], report['code']) != ('NotSupportedException', 'Fragment.ControlOwner'):
        raise ValueError('Protected cell destination no longer rejects')
    if report['docxDiagnostics'] != [] or report['pdfDiagnostics'] != ['Control.StaticDisplay:Info']:
        raise ValueError('Output diagnostics changed')
    expected_handles = [{'Id': 1, 'Tag': 'host', 'Owner': 'inline body/cell paragraph', 'DisplayText': 'Host'},
                        {'Id': 2, 'Tag': 'plain-cell', 'Owner': 'block body/cell paragraphs', 'DisplayText': 'Alpha'},
                        {'Id': 3, 'Tag': 'rich-cell', 'Owner': 'block body/cell paragraphs', 'DisplayText': 'Bravo'}]
    if report['beforeEdit'] != expected_handles:
        raise ValueError('Inserted control handles or owners changed')
    source = [{'id': 1, 'tag': 'plain-cell', 'kind': 'text', 'value': 'Alpha'},
              {'id': 2, 'tag': 'rich-cell', 'kind': 'richText', 'value': 'Bravo'}]
    composed = [{'id': 1, 'tag': 'host', 'kind': 'text', 'value': 'Host'},
                {'id': 2, 'tag': 'plain-cell', 'kind': 'text', 'value': 'Edited'},
                {'id': 3, 'tag': 'rich-cell', 'kind': 'richText', 'value': 'Bravo'}]
    if data['source']['controls'] != source or data['composed']['controls'] != composed:
        raise ValueError('Control identity, value or ordering changed')
    if data['source']['cellWrapperCounts'] != [1, 1, 0, 0, 0, 0] or data['composed']['cellWrapperCounts'] != [1, 1, 0, 0, 0, 0]:
        raise ValueError('Cell ownership changed')
    if data['source']['text'] != 'SourceBeforeAlphaBravoSourceAfter' or data['composed']['text'] != 'HostStartHostSourceBeforeEditedBravoSourceAfterHostEnd':
        raise ValueError('DOCX content or order changed')
    expected = ['HostStartHost', 'SourceBefore', 'Edited', 'Bravo', 'SourceAfter', 'HostEnd']
    if data['officeText'] != expected or data['wordnetText'] != expected:
        raise ValueError('Independent or generated PDF content/order changed')
    return {'inputs': 3, 'schemaValidInputs': 3, 'schemaValidOutputs': 2,
            'createdControlsComposeWithUniqueIds': True, 'sourceAndRejectedDestinationUnchanged': True,
            'independentAndGeneratedPdfTextMatch': True,
            'limitations': 'The protected cell destination rejects table-bearing control fragments; arbitrary owners and interactive Word editing remain unqualified.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('fixtures', 'observed', 'office', 'assembly', 'input_log', 'output_log'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    data = collect(args.fixtures, args.observed, args.office, args.assembly, args.input_log, args.output_log)
    result = check(data); controls = {}
    names = ('fixture changed', 'assembly changed', 'input schema omitted', 'output schema omitted', 'output changed',
             'source changed', 'destination changed', 'protected owner accepted', 'diagnostic omitted',
             'id collision', 'wrong control kind', 'value changed', 'cell owner changed', 'source order changed',
             'office order changed', 'generated pdf changed', 'control handle changed')
    for name in names:
        altered = copy.deepcopy(data); report = altered['report']
        if name == 'fixture changed': altered['inputs']['source.docx'] = '0' * 64
        elif name == 'assembly changed': altered['assembly'] = '0' * 64
        elif name == 'input schema omitted': altered['inputSchema'] = ''
        elif name == 'output schema omitted': altered['outputSchema'] = ''
        elif name == 'output changed': altered['pdfHash'] = '0' * 64
        elif name == 'source changed': report['sourceUnchanged'] = False
        elif name == 'destination changed': report['rejectedTargetUnchanged'] = False
        elif name == 'protected owner accepted': report['code'] = 'none'
        elif name == 'diagnostic omitted': report['pdfDiagnostics'] = []
        elif name == 'id collision': altered['composed']['controls'][1]['id'] = 1
        elif name == 'wrong control kind': altered['composed']['controls'][2]['kind'] = 'text'
        elif name == 'value changed': altered['composed']['controls'][1]['value'] = 'Alpha'
        elif name == 'cell owner changed': altered['composed']['cellWrapperCounts'][0] = 0
        elif name == 'source order changed': altered['source']['text'] = 'wrong'
        elif name == 'office order changed': altered['officeText'].reverse()
        elif name == 'generated pdf changed': altered['wordnetText'].pop()
        elif name == 'control handle changed': report['beforeEdit'][1]['Owner'] = 'unexpected'
        try:
            check(altered)
        except (ValueError, KeyError, IndexError, TypeError) as error:
            controls[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'result': result, 'negativeControls': controls,
                      'controlMethod': 'Independently parsed observations were altered in memory.'}, indent=2))


if __name__ == '__main__':
    main()
