#!/usr/bin/env python3
"""Compare independently parsed table-field templates with inspection and merge."""
import argparse
import copy
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'


def document(path):
    with ZipFile(path) as package:
        return ET.fromstring(package.read('word/document.xml'))


def field_paths(root):
    return [item.get(W + 'instr', '').removeprefix('MERGEFIELD ') for item in root.iter(W + 'fldSimple')]


def read(sources, outputs):
    values = {}
    for name in ('simple', 'merged', 'nested'):
        source = document(sources / (name + '.docx'))
        body = source.find(W + 'body')
        tables = body.findall(W + 'tbl')
        if len(tables) != 1:
            raise ValueError('Expected one direct source table')
        table = tables[0]
        result = json.loads((outputs / name / 'result.json').read_text(encoding='utf-8'))
        default = (outputs / name / 'default.json').read_text(encoding='utf-8')
        values[name] = {'fields': field_paths(source), 'result': result, 'default': default,
                        'hasSpan': table.find('.//' + W + 'gridSpan') is not None,
                        'nestedTables': len(table.findall('.//' + W + 'tbl'))}
    merged = document(outputs / 'simple' / 'merged.docx')
    body = merged.find(W + 'body')
    paragraph, cell = body.find(W + 'p'), body.find('./' + W + 'tbl/' + W + 'tr/' + W + 'tc')
    values['simple']['saved'] = [''.join(node.text or '' for node in paragraph.iter(W + 't')),
                                  ''.join(node.text or '' for node in cell.iter(W + 't'))]
    values['simple']['savedFields'] = field_paths(merged)
    return values


def check(values):
    if set(values) != {'simple', 'merged', 'nested'}:
        raise ValueError('Unexpected fixture set')
    simple = values['simple']; report = simple['result']
    if simple['fields'] != ['Customer.Name', 'Customer.Code'] or simple['hasSpan'] or simple['nestedTables']:
        raise ValueError('Simple source field or owner structure changed')
    if report['IsReusable'] or not report['rejected'] or report['changes'] != 2:
        raise ValueError('Inspection reuse or observed merge outcome changed')
    if report['before'] != ['cached', 'cached'] or report['afterFailure'] != report['before']:
        raise ValueError('Failed merge changed the document')
    if report['afterMerge'] != ['Ada', 'Q42'] or simple['saved'] != ['Ada', 'Q42'] or simple['savedFields']:
        raise ValueError('Successful merge did not produce the expected independent XML text')
    requirements = {item['path']: item for item in report['requirements']}
    if set(requirements) != set(simple['fields']) or requirements['Customer.Name']['DeclaredType'] != 0 or not requirements['Customer.Name']['Required']:
        raise ValueError('Inspected body and cell requirements differ from source XML')
    if requirements['Customer.Code']['DeclaredType'] is not None or requirements['Customer.Code']['Required'] is not None:
        raise ValueError('Missing declaration was assigned a type or requiredness')
    missing = [item for item in report['findings'] if item['Code'] == 'Plan.MissingPath']
    if len(missing) != 1 or missing[0]['path'] != 'Customer.Code' or missing[0]['part'] != 'word/document.xml' or missing[0]['Unknown']:
        raise ValueError('Missing cell-path prediction differs from the source')
    for name in ('simple', 'merged', 'nested'):
        value = values[name]; findings = value['result']['findings']
        for code in ('Plan.UninspectedCoverage', 'Plan.ReuseUnavailable', 'Plan.OwnerCoverage'):
            if not any(item['Code'] == code and item['Unknown'] for item in findings):
                raise ValueError('Uninspected coverage was not retained')
        if value['result']['IsReusable'] or 'word/document.xml' in value['default'] or any(path in value['default'] for path in value['fields']):
            raise ValueError('Default output disclosed a path or claimed reuse')
    if not values['merged']['hasSpan'] or values['merged']['fields'] != ['Hidden']:
        raise ValueError('Merged-cell negative source changed')
    if values['nested']['nestedTables'] != 1 or values['nested']['fields'] != ['HiddenNested']:
        raise ValueError('Nested-table negative source changed')
    for name in ('merged', 'nested'):
        value = values[name]['result']
        if value['requirements'] or any(item['Code'] == 'Plan.MissingPath' for item in value['findings']):
            raise ValueError('An unsupported table owner was treated as inspected')
    return {'simpleRequirements': len(requirements), 'missingCellPaths': len(missing), 'successfulMergeChanges': report['changes'],
            'failedMergeUnchanged': True, 'unsupportedOwnersUnknown': ['merged', 'nested'], 'defaultNamesDisclosed': False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('sources', type=Path)
    parser.add_argument('outputs', type=Path)
    args = parser.parse_args()
    values = read(args.sources, args.outputs)
    result = check(values)
    negatives = {}
    for name in ('changed source field', 'missing requirement', 'wrong prediction', 'reused inspection', 'failed merge mutated',
                 'wrong saved cell text', 'leaked default path', 'fabricated merged requirement', 'lost unknown coverage'):
        value = copy.deepcopy(values)
        if name == 'changed source field': value['simple']['fields'][1] = 'Other'
        if name == 'missing requirement': value['simple']['result']['requirements'].pop(0)
        if name == 'wrong prediction': next(item for item in value['simple']['result']['findings'] if item['Code'] == 'Plan.MissingPath')['path'] = 'Other'
        if name == 'reused inspection': value['simple']['result']['IsReusable'] = True
        if name == 'failed merge mutated': value['simple']['result']['afterFailure'][1] = 'changed'
        if name == 'wrong saved cell text': value['simple']['saved'][1] = 'changed'
        if name == 'leaked default path': value['simple']['default'] += 'Customer.Code'
        if name == 'fabricated merged requirement': value['merged']['result']['requirements'].append({'path': 'Hidden'})
        if name == 'lost unknown coverage': value['nested']['result']['findings'] = [item for item in value['nested']['result']['findings'] if item['Code'] != 'Plan.OwnerCoverage']
        try:
            check(value)
        except (ValueError, KeyError, TypeError, IndexError) as error:
            negatives[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'result': result, 'negativeControls': negatives,
                      'controlMethod': 'Parsed source/output observations changed in memory.'}, indent=2))


if __name__ == '__main__':
    main()
