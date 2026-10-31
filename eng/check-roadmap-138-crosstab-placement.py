#!/usr/bin/env python3
"""Check placed table values, reading order and independent PDF geometry."""

import argparse
import json
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('docx', type=Path)
    parser.add_argument('pdf_text', type=Path)
    parser.add_argument('pdf_bbox', type=Path)
    args = parser.parse_args()
    w = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
    expected = [['Row', 'Q1', 'Q2', 'Total'], ['North', '3.5', '0.0', '3.5'],
                ['South', '0.0', '4.0', '4.0'], ['Total', '3.5', '4.0', '7.5']]
    with zipfile.ZipFile(args.docx) as document:
        root = ET.fromstring(document.read('word/document.xml'))
        body = root.find(w + 'body')
        assert [item.tag for item in body if item.tag != w + 'sectPr'] == [w + 'p', w + 'tbl', w + 'p']
        assert ''.join(body[0].itertext()) == 'Before' and ''.join(body[2].itertext()) == 'After'
        table = body.find(w + 'tbl')
        rows = [[''.join(cell.itertext()) for cell in row.findall(w + 'tc')] for row in table.findall(w + 'tr')]
        assert rows == expected
        assert table.find(w + 'tr/' + w + 'trPr/' + w + 'tblHeader') is not None
        assert len(table.find(w + 'tblGrid').findall(w + 'gridCol')) == 4
        assert not root.findall('.//' + w + 'bookmarkStart')
    text = args.pdf_text.read_text(encoding='utf-8')
    assert text.index('Before') < text.index('North') < text.index('South') < text.index('After')
    assert all(value in text for value in ('Q1', 'Q2', '7.5'))
    bbox = ET.parse(args.pdf_bbox).getroot()
    words = [(item.text, {key: float(value) for key, value in item.attrib.items()})
             for item in bbox.iter() if item.tag.endswith('word') and item.text]
    def one(value):
        matches = [box for text, box in words if text == value]
        assert len(matches) == 1
        return matches[0]
    before, north, south, after = (one(value) for value in ('Before', 'North', 'South', 'After'))
    assert before['yMin'] < north['yMin'] < south['yMin'] < after['yMin']
    headers = [one(value) for value in ('Row', 'Q1', 'Q2')]
    assert headers[0]['xMin'] < headers[1]['xMin'] < headers[2]['xMin']
    print(json.dumps({'cells': expected, 'headerX': [item['xMin'] for item in headers],
                      'rowY': [item['yMin'] for item in (before, north, south, after)]}))


if __name__ == '__main__':
    main()
