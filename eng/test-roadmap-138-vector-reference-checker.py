"""Reject corrupted editable alternatives and substituted vector geometry."""
import argparse
from pathlib import Path
import subprocess
import sys
from xml.etree import ElementTree as ET
from zipfile import ZipFile

WP = '{http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing}'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('saved', type=Path)
    parser.add_argument('actual', type=Path)
    parser.add_argument('reference', type=Path)
    parser.add_argument('wrong_reference', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError('Choose a new scratch output path.')
    with ZipFile(args.saved) as original, ZipFile(args.output, 'w') as changed:
        for entry in original.infolist():
            data = original.read(entry.filename)
            if entry.filename == 'word/document.xml':
                root = ET.fromstring(data)
                root.find('.//' + WP + 'docPr').set('descr', 'Changed diagram')
                data = ET.tostring(root, encoding='utf-8', xml_declaration=True)
            changed.writestr(entry, data)
    checker = Path(__file__).with_name('check-roadmap-138-vector-reference.py')
    cases = [(args.output, args.reference, 'The editable shape or description changed.'),
             (args.saved, args.wrong_reference, 'exceeds the original 0.5-point geometry tolerance')]
    for saved, reference, reason in cases:
        result = subprocess.run([sys.executable, str(checker), str(args.source), str(saved), str(args.actual), str(reference)], capture_output=True)
        if result.returncode == 0 or reason not in result.stderr.decode('utf-8'):
            raise ValueError('A negative control did not fail for its intended reason: ' + reason)
    print('Independent checker rejected changed alternative text and misplaced vector geometry.')


if __name__ == '__main__':
    main()
