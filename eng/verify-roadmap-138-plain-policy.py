#!/usr/bin/env python3
"""Check a narrow DOCX policy-source prototype with independent schema and PDF readers."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import re

NAMES = ('short', 'maximum', 'single', 'lowercase', 'mixed')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def collect(fixtures, observed, independent, assembly, schema_log):
    return {'manifest': json.loads((fixtures / 'manifest.json').read_text()),
            'inputs': {path.name: sha(path) for path in fixtures.glob('*.docx')},
            'report': json.loads((observed / 'report.json').read_text()), 'assembly': sha(assembly),
            'outputs': {path.stem: sha(path) for path in observed.glob('*.pdf')},
            'schema': schema_log.read_text(),
            'pdfText': {name: (independent / (name + '.txt')).read_text().split() for name in NAMES},
            'pdfInfo': {name: (independent / (name + '-info.txt')).read_text() for name in NAMES},
            'pdfFonts': {name: (independent / (name + '-fonts.txt')).read_text() for name in NAMES}}


def check(data):
    expected = {'short': 'Alpha123', 'maximum': 'ABCDEFGHIJKLMNOPQRSTUVWXYZ12345678901234', 'single': 'A',
                'lowercase': 'abcdefghijklmnopqrstuvwxyz', 'mixed': 'A0b1C2d3E4f5G6h7I8j9K0l1M2n3O4p5Q6r7S8t9'}
    rejected = {'field', 'formatted', 'long', 'space', 'styles', 'two-paragraphs'}
    report = data['report']
    if data['manifest']['expectedAcceptedText'] != expected or len(data['inputs']) != 11 or data['manifest']['documents'] != data['inputs']:
        raise ValueError('Independent input manifest changed')
    if report['coreSha256'] != data['assembly'] or set(report['cases']) != set(expected) | rejected:
        raise ValueError('Loaded core or scenario set changed')
    for name, digest in data['inputs'].items():
        if name + ' ' + digest + ' errors=0' not in data['schema']:
            raise ValueError('Independent input schema validation missing')
        case = report['cases'][name[:-5]]
        if case['sourceSha256'] != digest or case['streamOpen'] is not True:
            raise ValueError('Source identity or stream ownership changed')
    for name in rejected:
        if report['cases'][name]['result'] != 'Plan.SourceProfile':
            raise ValueError('Out-of-profile source was admitted')
    if set(data['outputs']) != set(expected):
        raise ValueError('Unexpected output set')
    for name, text in expected.items():
        case = report['cases'][name]
        if case['result'] != 'accepted' or case['diagnostics'] != [] or case['outputSha256'] != data['outputs'][name]:
            raise ValueError('Accepted conversion result changed')
        if data['pdfText'][name] != [text] or not re.search(r'^Pages:\s+1\s*$', data['pdfInfo'][name], re.MULTILINE):
            raise ValueError('Independent PDF text or page count changed')
        lines = data['pdfFonts'][name].splitlines()
        if len(lines) != 3 or lines[2].split()[:6] != ['NotoSans-Regular', 'TrueType', 'WinAnsi', 'yes', 'no', 'yes']:
            raise ValueError('Independent PDF font observation changed')
    return {'schemaValidInputs': 11, 'acceptedPlainInputs': 5, 'rejectedStructuredInputs': 6,
            'independentPdfTextAndPageCountMatch': True,
            'limitations': 'Uncommitted source-profile prototype only; these PDFs do not establish policy execution or general template support.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('fixtures', 'observed', 'independent', 'assembly', 'schema_log'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    data = collect(args.fixtures, args.observed, args.independent, args.assembly, args.schema_log)
    result = check(data)
    controls = {}
    mutations = {
        'manifest': lambda d: d['manifest']['documents'].update({'short.docx': '0' * 64}),
        'source identity': lambda d: d['report']['cases']['short'].update(sourceSha256='0' * 64),
        'core assembly': lambda d: d.update(assembly='0' * 64),
        'schema validation': lambda d: d.update(schema=''),
        'stream ownership': lambda d: d['report']['cases']['short'].update(streamOpen=False),
        'field admitted': lambda d: d['report']['cases']['field'].update(result='accepted'),
        'format admitted': lambda d: d['report']['cases']['formatted'].update(result='accepted'),
        'style admitted': lambda d: d['report']['cases']['styles'].update(result='accepted'),
        'extra output': lambda d: d['outputs'].update(field='0' * 64),
        'missing output': lambda d: d['outputs'].pop('short'),
        'output hash': lambda d: d['report']['cases']['short'].update(outputSha256='0' * 64),
        'diagnostic': lambda d: d['report']['cases']['short'].update(diagnostics=[{'Code': 'Unexpected'}]),
        'pdf text': lambda d: d['pdfText'].update(short=['changed']),
        'page count': lambda d: d['pdfInfo'].update(short='Pages: 2'),
        'font': lambda d: d['pdfFonts'].update(short='missing'),
    }
    for name, mutate in mutations.items():
        altered = copy.deepcopy(data)
        mutate(altered)
        try:
            check(altered)
        except (ValueError, KeyError, IndexError, TypeError) as error:
            controls[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'result': result, 'negativeControls': controls,
                      'controlMethod': 'Independent observations were altered in memory.'}, indent=2))


if __name__ == '__main__':
    main()
