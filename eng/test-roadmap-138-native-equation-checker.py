"""Confirm the independent checker rejects a changed fraction and a missing one."""
import argparse
from pathlib import Path
import subprocess
import sys
from xml.etree import ElementTree as ET
from zipfile import ZipFile

M = '{http://schemas.openxmlformats.org/officeDocument/2006/math}'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('saved', type=Path)
    parser.add_argument('actual', type=Path)
    parser.add_argument('reference', type=Path)
    parser.add_argument('missing_reference', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError('Choose a new scratch output path.')
    with ZipFile(args.saved) as original, ZipFile(args.output, 'w') as changed:
        for entry in original.infolist():
            data = original.read(entry.filename)
            if entry.filename == 'word/document.xml':
                root = ET.fromstring(data)
                root.find('.//' + M + 'den/' + M + 'r/' + M + 't').text = '3'
                data = ET.tostring(root, encoding='utf-8', xml_declaration=True)
            changed.writestr(entry, data)
    checker = Path(__file__).with_name('check-roadmap-138-native-equation.py')
    cases = [(args.source, args.output, args.actual, args.reference, 'The editable fraction changed.'),
             (args.source, args.saved, args.actual, args.missing_reference, 'Missing expected source or reference text.')]
    for source, saved, actual, reference, expected in cases:
        result = subprocess.run([sys.executable, str(checker), str(source), str(saved), str(actual), str(reference)], capture_output=True)
        if result.returncode == 0 or expected not in result.stderr.decode('utf-8'):
            raise ValueError('A negative control did not fail for its intended reason: ' + expected)
    print('Independent checker rejected a changed denominator and an omitted-reference equation.')


if __name__ == '__main__':
    main()
