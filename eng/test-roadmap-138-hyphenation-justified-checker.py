"""Reject an unmodified left-aligned substitute and a changed reference source."""
import argparse
from pathlib import Path
import subprocess
import sys
from xml.etree import ElementTree as ET
from zipfile import ZipFile

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('actual', type=Path); parser.add_argument('left', type=Path)
    parser.add_argument('source', type=Path); parser.add_argument('reference', type=Path); parser.add_argument('output', type=Path)
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError('Choose a new control path.')
    with ZipFile(args.source) as original, ZipFile(args.output, 'w') as changed:
        for entry in original.infolist():
            data = original.read(entry.filename)
            if entry.filename == 'word/document.xml':
                root = ET.fromstring(data); next(root.iter(W + 't')).text = 'Changed reference'
                data = ET.tostring(root, encoding='utf-8', xml_declaration=True)
            changed.writestr(entry, data)
    checker = Path(__file__).with_name('check-roadmap-138-hyphenation-justified.py')
    cases = [(args.left, args.left, args.source, 'The justified word did not move'),
             (args.actual, args.left, args.output, 'The independently authored soft-hyphen reference changed.')]
    for actual, left, source, reason in cases:
        result = subprocess.run([sys.executable, str(checker), str(actual), str(left), str(source), str(args.reference)], capture_output=True)
        if result.returncode == 0 or reason not in result.stderr.decode('utf-8'):
            raise ValueError('A corrupted control did not fail for the intended reason: ' + reason)
    print('Independent justified checker rejected left-aligned and altered-source controls.')


if __name__ == '__main__':
    main()
