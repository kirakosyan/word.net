"""Confirm the preset checker rejects changed editable and visual geometry."""
import argparse
from pathlib import Path
import subprocess
import sys
from xml.etree import ElementTree as ET
from zipfile import ZipFile

A = '{http://schemas.openxmlformats.org/drawingml/2006/main}'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError('Choose a new scratch output path.')
    base = args.directory
    saved = base / 'wordnet-final/triangle.docx'
    with ZipFile(saved) as original, ZipFile(args.output, 'w') as changed:
        for entry in original.infolist():
            data = original.read(entry.filename)
            if entry.filename == 'word/document.xml':
                root = ET.fromstring(data)
                root.find('.//' + A + 'prstGeom').set('prst', 'rect')
                data = ET.tostring(root, encoding='utf-8', xml_declaration=True)
            changed.writestr(entry, data)
    checker = Path(__file__).with_name('check-roadmap-138-preset-shapes.py')
    common = [sys.executable, str(checker), 'triangle', str(base / 'triangle.docx')]
    actual = base / 'wordnet-final/triangle.pdf'
    reference = base / 'office/triangle.pdf'
    tail = [str(base / 'wordnet-final/triangle.svg'), str(base / 'wordnet-final/triangle.xps')]
    cases = [(args.output, reference, 'The editable preset or its owner changed.'),
             (saved, base / 'office/diamond.pdf', 'reference PDF vertex or edge exceeds')]
    for docx, pdf, expected in cases:
        result = subprocess.run(common + [str(docx), str(actual), str(pdf)] + tail, capture_output=True)
        if result.returncode == 0 or expected not in result.stderr.decode('utf-8'):
            raise ValueError('A negative control did not fail for its intended reason: ' + expected)
    print('Independent checker rejected a changed preset and a substituted reference polygon.')


if __name__ == '__main__':
    main()
