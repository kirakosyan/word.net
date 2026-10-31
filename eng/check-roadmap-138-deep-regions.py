"""Independently check the nested report's OOXML and extracted PDF text."""
import argparse
import json
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
EXPECTED = ['Before', 'North', 'A', 'Apple', 'North', 'Root', 'Pear', 'North', 'Root',
            'End group', 'Empty', 'End group', 'End customer', 'South', 'B', 'Plum',
            'South', 'Root', 'End group', 'End customer', 'After']


def document(path):
    with zipfile.ZipFile(path) as archive:
        return ET.fromstring(archive.read('word/document.xml'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('preserved', type=Path)
    parser.add_argument('expanded', type=Path)
    parser.add_argument('wordnet_text', type=Path)
    parser.add_argument('reference_text', type=Path)
    args = parser.parse_args()
    original = document(args.preserved)
    control = original.find('.//' + W + 'sdt')
    tags = []
    while control is not None:
        properties = control.find(W + 'sdtPr')
        tags.append((properties.find(W + 'tag').get(W + 'val'), properties.find(W + 'id').get(W + 'val')))
        control = control.find('./' + W + 'sdtContent/' + W + 'sdt')
    if tags != [('repeat:Orders', '10'), ('repeat:Groups', '20'), ('repeat:Units', '30')]:
        raise ValueError(f'Unexpected preserved hierarchy: {tags}')
    expanded = document(args.expanded)
    if expanded.findall('.//' + W + 'sdt'):
        raise ValueError('Expanded output retains a repeat wrapper.')
    starts = expanded.findall('.//' + W + 'bookmarkStart')
    ends = expanded.findall('.//' + W + 'bookmarkEnd')
    ids = [entry.get(W + 'id') for entry in starts]
    names = [entry.get(W + 'name') for entry in starts]
    if len(starts) != 3 or len(set(ids)) != 3 or len(set(names)) != 3 or set(ids) != {entry.get(W + 'id') for entry in ends}:
        raise ValueError('Expanded bookmark identities are missing, duplicated or unmatched.')
    body = expanded.find(W + 'body')
    blocks = [element for element in body if element.tag in (W + 'p', W + 'tbl')]
    values = [''.join(node.text or '' for node in element.iter(W + 't')) for element in blocks]
    if values != EXPECTED:
        raise ValueError(f'Unexpected document order: {values}')
    for path in (args.wordnet_text, args.reference_text):
        lines = [line.strip() for line in path.read_text(encoding='utf-8').splitlines() if line.strip()]
        if lines != EXPECTED:
            raise ValueError(f'Unexpected extracted text in {path}: {lines}')
    print(json.dumps({'hierarchy': tags, 'bookmarks': names, 'orderedBlocks': len(values), 'pdfTextValues': len(EXPECTED)}))


if __name__ == '__main__':
    main()
