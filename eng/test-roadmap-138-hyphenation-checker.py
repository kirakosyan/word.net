"""Check that changed editable content and a blank PDF do not pass the oracle."""
import argparse
from pathlib import Path
import subprocess
import sys
from xml.etree import ElementTree as ET
from zipfile import ZipFile

import pymupdf

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path); parser.add_argument('saved', type=Path)
    parser.add_argument('pdf', type=Path); parser.add_argument('font', type=Path); parser.add_argument('output', type=Path)
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError('Choose a new control directory.')
    args.output.mkdir(parents=True)
    changed = args.output / 'changed.docx'
    with ZipFile(args.saved) as original, ZipFile(changed, 'w') as output:
        for entry in original.infolist():
            data = original.read(entry.filename)
            if entry.filename == 'word/document.xml':
                root = ET.fromstring(data); next(root.iter(W + 't')).text = 'Changed text'
                data = ET.tostring(root, encoding='utf-8', xml_declaration=True)
            output.writestr(entry, data)
    blank = args.output / 'blank.pdf'
    with pymupdf.open(args.pdf) as pdf:
        page = pdf[0]; page.clean_contents(); pdf.update_stream(page.get_contents()[0], b''); pdf.save(blank)
    checker = Path(__file__).with_name('check-roadmap-138-hyphenation.py')
    for saved, pdf, reason in [(changed, args.pdf, 'Editable source text changed.'),
                               (args.saved, blank, 'Extracted logical text changed')]:
        result = subprocess.run([sys.executable, str(checker), str(args.source), str(saved), str(pdf), str(args.font)], capture_output=True)
        if result.returncode == 0 or reason not in result.stderr.decode('utf-8'):
            raise ValueError('A corrupted control did not fail for the expected reason: ' + reason)
    print('Independent checker rejected changed editable text and removed PDF content.')


if __name__ == '__main__':
    main()
