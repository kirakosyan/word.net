#!/usr/bin/env python3
"""Compare PDF-option findings with staged export and independent PDF text."""
import argparse
import copy
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile
import fitz

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
EXPECTED = {
    'a2a': ('Plan.PdfTaggedArchiveConflict', 'NotSupportedException', 'Pdf.TaggedArchive'),
    'tagged_a2b': ('Plan.PdfTaggedArchiveConflict', 'NotSupportedException', 'Pdf.TagsConformance'),
    'native_archive': ('Plan.PdfNativeArchiveConflict', 'NotSupportedException', 'Typography.Conformance'),
    'invalid': ('Plan.PdfOptionsInvalid', 'ArgumentOutOfRangeException', None),
    'missing': ('Plan.PdfOptionsMissing', 'ArgumentException', None),
}


def check(values, source_text, pdf_text):
    if source_text != 'Synthetic sample' or pdf_text.strip() != source_text:
        raise ValueError('Independent source and successful PDF text differ')
    valid = values['valid']['result']
    if valid['errorType'] is not None or valid['unchanged'] or any(f['Code'].startswith('Plan.Pdf') for f in valid['findings']):
        raise ValueError('Valid profile did not produce an unflagged PDF')
    for name, (code, error_type, error_code) in EXPECTED.items():
        result = values[name]['result']; findings = [f for f in result['findings'] if f['Code'] == code]
        if len(findings) != 1 or findings[0]['Unknown'] or findings[0]['PartId'] is not None or findings[0]['SourcePhase'] is not None:
            raise ValueError('Missing or misleading static profile finding: ' + name)
        if result['errorType'] != error_type or error_code is not None and result['errorCode'] != error_code or not result['unchanged']:
            raise ValueError('Profile rejection or unchanged staged output differs: ' + name)
    for value in values.values():
        result = value['result']; serialized = json.dumps(value['default'], sort_keys=True)
        if result['inspectionClockCalls'] != 0 or result['IsReusable']:
            raise ValueError('Inspection evaluated the clock or became reusable')
        if not any(f['Code'] == 'Plan.UninspectedCoverage' and f['Unknown'] for f in result['findings']):
            raise ValueError('Unperformed stages lost their uncertainty')
        if 'Synthetic sample' in serialized or 'word/document.xml' in serialized:
            raise ValueError('Default report disclosed source content or part names')
    return {'rejectedProfiles': len(EXPECTED), 'failedOutputsUnchanged': True,
            'validPdfTextMatches': True, 'inspectionClockCalls': 0, 'laterStagesUnknown': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('outputs', type=Path)
    args = parser.parse_args()
    with ZipFile(args.source) as package:
        root = ET.fromstring(package.read('word/document.xml'))
    source_text = ''.join(element.text or '' for element in root.iter(W + 't'))
    with fitz.open(args.outputs / 'valid/output.pdf') as document:
        pdf_text = ''.join(page.get_text() for page in document)
    names = ('valid', 'a2a', 'tagged_a2b', 'native_archive', 'invalid', 'missing')
    values = {name: {'default': json.loads((args.outputs / name / 'default.json').read_text(encoding='utf-8')),
                     'result': json.loads((args.outputs / name / 'result.json').read_text(encoding='utf-8'))} for name in names}
    result = check(values, source_text, pdf_text)
    negatives = {}
    for name in ('wrong conflict', 'unknown conflict', 'invented phase', 'changed error', 'mutated output',
                 'clock evaluated', 'lost uncertainty', 'leaked source', 'changed valid PDF', 'reusable report'):
        altered, text = copy.deepcopy(values), pdf_text
        a2a = altered['a2a']['result']
        if name == 'wrong conflict': next(f for f in a2a['findings'] if f['Code'] == 'Plan.PdfTaggedArchiveConflict')['Code'] = 'Other'
        if name == 'unknown conflict': next(f for f in a2a['findings'] if f['Code'] == 'Plan.PdfTaggedArchiveConflict')['Unknown'] = True
        if name == 'invented phase': next(f for f in a2a['findings'] if f['Code'] == 'Plan.PdfTaggedArchiveConflict')['SourcePhase'] = 3
        if name == 'changed error': a2a['errorCode'] = 'Other'
        if name == 'mutated output': a2a['unchanged'] = False
        if name == 'clock evaluated': a2a['inspectionClockCalls'] = 1
        if name == 'lost uncertainty': a2a['findings'] = [f for f in a2a['findings'] if f['Code'] != 'Plan.UninspectedCoverage']
        if name == 'leaked source': altered['a2a']['default']['extra'] = 'Synthetic sample'
        if name == 'changed valid PDF': text = 'changed'
        if name == 'reusable report': a2a['IsReusable'] = True
        try:
            check(altered, source_text, text)
        except (ValueError, KeyError, TypeError, IndexError) as error:
            negatives[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'result': result, 'negativeControls': negatives,
                      'controlMethod': 'Parsed source/output observations changed in memory.'}, indent=2))


if __name__ == '__main__':
    main()
