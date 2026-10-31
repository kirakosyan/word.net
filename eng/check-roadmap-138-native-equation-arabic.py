"""Retain the observed directional extraction difference around a fraction."""
import argparse
import json
from pathlib import Path
import subprocess
from xml.etree import ElementTree as ET
from zipfile import ZipFile

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
M = '{http://schemas.openxmlformats.org/officeDocument/2006/math}'


def source_text(path):
    with ZipFile(path) as document:
        root = ET.fromstring(document.read('word/document.xml'))
    text = [item.text or '' for item in root.iter(W + 't')]
    if text != ['Before مرحبا', 'After مرحبا'] or len(root.findall('.//' + M + 'f')) != 1:
        raise ValueError('The editable Arabic paragraphs or fraction changed.')


def text(path):
    return subprocess.check_output(['pdftotext', '-layout', str(path), '-']).decode('utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('saved', type=Path)
    parser.add_argument('actual', type=Path)
    parser.add_argument('reference', type=Path)
    args = parser.parse_args()
    source_text(args.source)
    source_text(args.saved)
    actual, reference = text(args.actual), text(args.reference)
    if actual.count('ابحرم') != 2 or reference.count('مرحبا') != 2:
        raise ValueError('The recorded Arabic extraction difference changed; review the observation.')
    if not (actual.index('Before') < actual.index('(x)/(2)') < actual.index('After')
            and reference.index('Before') < reference.index('\nx\n2\n') < reference.index('After')):
        raise ValueError('The native or reference object order changed.')
    print(json.dumps({'nativePopplerWord': 'ابحرم', 'referencePopplerWord': 'مرحبا',
                      'paragraphsObserved': 2, 'directionalExtractionPassed': False,
                      'note': 'The Arabic logical source remains editable; the independent extraction difference is retained.'}, ensure_ascii=False))


if __name__ == '__main__':
    main()
