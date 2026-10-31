"""Extract selected public Word-authored controls and local glossary resources."""
import argparse
import hashlib
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile

SOURCE_SHA256 = '926024b1393e5293c9736b562343ef3f357e8801956d6c0690fb2178603bf626'
W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
CONTROLS = {0: 'rich-block.xml', 1: 'rich-inline.xml', 4: 'plain-block.xml',
            7: 'combo-dangling.xml', 8: 'dropdown-dangling.xml', 9: 'date-value.xml', 10: 'date-placeholder.xml'}
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
    parser.add_argument('--input', type=Path, required=True, help='Pinned public DOCX; this script never fetches it')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if hashlib.sha256(args.input.read_bytes()).hexdigest() != SOURCE_SHA256:
        parser.error('The input does not match the pinned upstream fixture hash.')
    args.output.mkdir(parents=True, exist_ok=True)
    with ZipFile(args.input) as archive:
        controls = ET.fromstring(archive.read('word/document.xml')).findall('.//' + W + 'sdt')
        if len(controls) != 12:
            parser.error('The pinned input must contain twelve controls.')
        for index, name in CONTROLS.items():
            controls[index].tail = None
            (args.output / name).write_bytes(ET.tostring(controls[index], encoding='utf-8') + b'\n')
        for source, name in PARTS.items():
            data = archive.read(source).decode('utf-8-sig').replace('\r\n', '\n').replace('\r', '\n')
            (args.output / name).write_text(data, encoding='utf-8', newline='\n')
    print('Extracted seven control subtrees and six glossary parts; other document content and metadata were not copied.')


if __name__ == '__main__':
    main()
