#!/usr/bin/env python3
"""Check authored simple-cell control fixtures against OOXML and independent PDF text."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
W14 = '{http://schemas.microsoft.com/office/word/2010/wordml}'
KINDS = [W + 'text', W14 + 'checkbox', W + 'dropDownList', W + 'comboBox', W + 'date', W + 'richText']
CREATED = ['Ada', 'X', 'Beta', 'Manual', '2026-10-31', 'Approved']
EDITED = ['Grace', '-', 'Alpha', 'New', '2027-01-01', 'Approved']


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inspect(path):
    with ZipFile(path) as package:
        root = ET.fromstring(package.read('word/document.xml'))
    body = root.find(W + 'body')
    tables = body.findall(W + 'tbl')
    if len(tables) != 1 or len(tables[0].findall(W + 'tr')) != 2:
        raise ValueError('Expected one two-row body table: ' + path.name)
    rows = tables[0].findall(W + 'tr')
    if any(len(row.findall(W + 'tc')) != 3 for row in rows):
        raise ValueError('Expected three direct cells per row: ' + path.name)
    result = []
    for row in rows:
        for cell in row.findall(W + 'tc'):
            controls = cell.findall(W + 'sdt')
            if len(controls) > 1:
                raise ValueError('Multiple wrappers in one cell')
            item = {'value': ''.join(text.text or '' for text in cell.iter(W + 't')), 'id': None, 'kind': None,
                    'paragraphs': len(cell.findall(W + 'p'))}
            if controls:
                control = controls[0]; properties = control.find(W + 'sdtPr'); content = control.find(W + 'sdtContent')
                identifiers = properties.findall(W + 'id')
                kinds = [kind for kind in KINDS if properties.find(kind) is not None]
                if len(identifiers) != 1 or len(kinds) != 1 or len(content.findall(W + 'p')) != 1:
                    raise ValueError('Control identity, type or paragraph ownership changed')
                item.update(id=int(identifiers[0].get(W + 'val')), kind=kinds[0].split('}')[-1],
                            paragraphs=len(content.findall(W + 'p')))
            result.append(item)
    return {'cells': result, 'bodyText': [''.join(x.text or '' for x in p.iter(W + 't')) for p in body.findall(W + 'p')]}


def collect(fixtures, observed, office, assembly, input_log, output_log):
    manifest = json.loads((fixtures / 'manifest.json').read_text())
    report = json.loads((observed / 'report.json').read_text())
    outputs = {}
    for name in ('created', 'edited'):
        outputs[name] = {'structure': inspect(observed / (name + '.docx')),
                         'docxHash': sha(observed / (name + '.docx')), 'pdfHash': sha(observed / (name + '.pdf')),
                         'officeText': (office / (name + '-office.txt')).read_text().split(),
                         'wordnetText': (office / (name + '-wordnet.txt')).read_text().split()}
    return {'manifest': manifest, 'inputs': {path.name: sha(path) for path in fixtures.glob('*.docx')},
            'report': report, 'outputs': outputs, 'assembly': sha(assembly),
            'inputSchema': input_log.read_text(), 'outputSchema': output_log.read_text()}


def check(data):
    if len(data['inputs']) != 4 or data['manifest']['documents'] != data['inputs'] or data['report']['inputHashes'] != data['inputs']:
        raise ValueError('Fixture identity changed')
    if data['report']['assemblySha256'] != data['assembly']:
        raise ValueError('Loaded core assembly identity changed')
    for name, digest in data['inputs'].items():
        if name + ' ' + digest + ' errors=0' not in data['inputSchema']:
            raise ValueError('Input schema evidence missing: ' + name)
    for name, expected in (('created', CREATED), ('edited', EDITED)):
        item = data['outputs'][name]; cells = item['structure']['cells']
        if item['structure']['bodyText'] != ['Before', 'After'] or len(cells) != 6:
            raise ValueError('Body or cell ownership changed: ' + name)
        if [cell['value'] for cell in cells] != expected or any(cell['paragraphs'] != 1 for cell in cells):
            raise ValueError('Cell values or paragraph ownership changed: ' + name)
        wanted_ids = [1, 2, 3, 4, 5, 6] if name == 'created' else [1, 2, 3, 4, 5, None]
        wanted_kinds = [kind.split('}')[-1] for kind in KINDS] if name == 'created' else [kind.split('}')[-1] for kind in KINDS[:5]] + [None]
        if [cell['id'] for cell in cells] != wanted_ids or [cell['kind'] for cell in cells] != wanted_kinds:
            raise ValueError('Control IDs, kinds or wrapper removal changed: ' + name)
        if (item['docxHash'], item['pdfHash']) != (data['report']['outputs'][name + 'Docx'], data['report']['outputs'][name + 'Pdf']):
            raise ValueError('Output identities changed: ' + name)
        if name + '.docx ' + item['docxHash'] + ' errors=0' not in data['outputSchema']:
            raise ValueError('Output schema evidence missing: ' + name)
        text = ['Before'] + expected + ['After']
        if item['officeText'] != text or item['wordnetText'] != text:
            raise ValueError('Independent or generated PDF text/order changed: ' + name)
        if data['report']['diagnostics'][name] != [] or data['report']['diagnostics'][name + 'Pdf'] != ['Control.StaticDisplay:Info']:
            raise ValueError('DOCX or static-display diagnostic changed: ' + name)
    if data['report']['retainedControlCount'] != 5:
        raise ValueError('Wrapper removal count changed')
    for name in ('styled', 'merged', 'nested'):
        item = data['report']['rejected'][name]
        if (item['exception'], item['code'], item['unchanged']) != ('NotSupportedException', 'Control.ProtectedOwner', True):
            raise ValueError('Rejected owner or atomicity changed: ' + name)
    return {'inputs': 4, 'schemaValidInputs': 4, 'schemaValidOutputs': 2, 'createdControls': 6,
            'editedControlsRetained': 5, 'protectedOwnersRejectedAtomically': 3,
            'independentAndGeneratedPdfTextMatch': True,
            'limitations': 'Static office text does not qualify interactive Word behavior, arbitrary table owners or full #145 acceptance.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('fixtures', 'observed', 'office', 'assembly', 'input_log', 'output_log'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    data = collect(args.fixtures, args.observed, args.office, args.assembly, args.input_log, args.output_log)
    result = check(data)
    controls = {}
    names = ('fixture changed', 'assembly changed', 'cell omitted', 'body changed', 'duplicate id', 'kind changed',
             'value changed', 'paragraph omitted', 'wrapper removal changed', 'retained count changed',
             'styled owner accepted', 'merged owner accepted', 'nested owner accepted', 'rejected input mutated',
             'output hash changed', 'schema omitted', 'office order changed', 'generated pdf changed', 'diagnostic omitted')
    for name in names:
        altered = copy.deepcopy(data); report = altered['report']; created = altered['outputs']['created']; edited = altered['outputs']['edited']
        cells = created['structure']['cells']
        if name == 'fixture changed': altered['inputs']['plain.docx'] = '0' * 64
        elif name == 'assembly changed': altered['assembly'] = '0' * 64
        elif name == 'cell omitted': cells.pop()
        elif name == 'body changed': created['structure']['bodyText'][0] = 'wrong'
        elif name == 'duplicate id': cells[1]['id'] = cells[0]['id']
        elif name == 'kind changed': cells[0]['kind'] = 'checkbox'
        elif name == 'value changed': cells[0]['value'] = 'wrong'
        elif name == 'paragraph omitted': cells[0]['paragraphs'] = 0
        elif name == 'wrapper removal changed': edited['structure']['cells'][5]['id'] = 6
        elif name == 'retained count changed': report['retainedControlCount'] = 6
        elif name.endswith('owner accepted'): report['rejected'][name.split()[0]]['code'] = 'none'
        elif name == 'rejected input mutated': report['rejected']['merged']['unchanged'] = False
        elif name == 'output hash changed': created['pdfHash'] = '0' * 64
        elif name == 'schema omitted': altered['outputSchema'] = ''
        elif name == 'office order changed': created['officeText'].reverse()
        elif name == 'generated pdf changed': edited['wordnetText'].pop()
        elif name == 'diagnostic omitted': report['diagnostics']['createdPdf'] = []
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
