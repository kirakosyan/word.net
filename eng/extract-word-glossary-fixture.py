"""Extract public, Word-authored glossary parts without retaining document metadata."""
import argparse
import hashlib
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile

SOURCE_SHA256 = '8d297bee1383f87d0fa77e7b2e5573e88517297a88f36f569ec5eaa0f8201878'
W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
PARTS = {
    'word/glossary/document.xml': 'glossary-document.xml',
    'word/glossary/_rels/document.xml.rels': 'glossary-relationships.xml',
    'word/glossary/styles.xml': 'glossary-styles.xml',
    'word/glossary/settings.xml': 'glossary-settings.xml',
    'word/glossary/fontTable.xml': 'glossary-font-table.xml',
    'word/glossary/webSettings.xml': 'glossary-web-settings.xml',
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True, help='The pinned public source DOCX; never fetched by this script')
    parser.add_argument('--output', type=Path, required=True, help='Directory for extracted XML parts')
    args = parser.parse_args()
    if hashlib.sha256(args.input.read_bytes()).hexdigest() != SOURCE_SHA256:
        parser.error('The input does not match the pinned upstream fixture hash.')
    args.output.mkdir(parents=True, exist_ok=True)
    with ZipFile(args.input) as archive:
        document = ET.fromstring(archive.read('word/document.xml'))
        controls = document.findall('.//' + W + 'sdt')
        if len(controls) != 1:
            parser.error('The pinned source must contain exactly one content control.')
        # ElementTree includes sibling-separating whitespace in the serialized tail.
        controls[0].tail = None
        (args.output / 'control.xml').write_bytes(ET.tostring(controls[0], encoding='utf-8') + b'\n')
        for source, name in PARTS.items():
            data = archive.read(source).decode('utf-8-sig').replace('\r\n', '\n').replace('\r', '\n')
            (args.output / name).write_text(data, encoding='utf-8', newline='\n')
    print('Extracted one control and six glossary parts; document metadata and other content were not copied.')


if __name__ == '__main__':
    main()
