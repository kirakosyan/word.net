#!/usr/bin/env python3
"""Check a Hindi merged-table specimen and report independent reference geometry."""

import argparse
import json
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('docx', type=Path)
parser.add_argument('reference_text', type=Path)
parser.add_argument('reference_boxes', type=Path)
parser.add_argument('native_text', type=Path)
parser.add_argument('native_boxes', type=Path)
args = parser.parse_args()
w = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
x = '{http://www.w3.org/1999/xhtml}'
with zipfile.ZipFile(args.docx) as document:
    main = ET.fromstring(document.read('word/document.xml'))
    assert [item.get(w + 'val') for item in main.findall('.//' + w + 'gridSpan')] == ['2', '2']
    assert [item.get(w + 'val') for item in main.findall('.//' + w + 'vMerge')] == ['restart', None]
    assert len(main.findall('.//' + w + 'tblHeader')) == 1
    assert ''.join(main.itertext()).find('हिन्दी क्षि') >= 0

labels = ['Invoice', 'Amount', 'हिन्दी', 'क्षि', 'A-100', '3.50', 'Detail', '7.25', 'Total', '10.75']


def inspect(text_path, boxes_path):
    text = text_path.read_text(encoding='utf-8')
    assert 'हिन्दी क्षि' in text
    for label in labels:
        assert text.count(label) == 1
    assert text.index('Invoice') < text.index('A-100') < text.index('Detail') < text.index('Total')
    words = ET.parse(boxes_path).findall('.//' + x + 'word')
    boxes = {}
    for label in labels:
        matches = [item for item in words if item.text == label]
        assert len(matches) == 1
        item = matches[0]
        boxes[label] = tuple(float(item.get(key)) for key in ('xMin', 'yMin', 'xMax', 'yMax'))
    assert boxes['Invoice'][0] < boxes['A-100'][0] < boxes['Amount'][0]
    assert boxes['हिन्दी'][0] < boxes['क्षि'][0] < boxes['क्षि'][2] < boxes['A-100'][0]
    assert boxes['Invoice'][1] < boxes['हिन्दी'][1] <= boxes['A-100'][1] + 1
    assert abs(boxes['हिन्दी'][1] - boxes['A-100'][1]) < 1
    assert boxes['A-100'][1] < boxes['Detail'][1] < boxes['Total'][1]
    return boxes


reference = inspect(args.reference_text, args.reference_boxes)
native = inspect(args.native_text, args.native_boxes)
print(json.dumps({'reference': reference, 'native': native,
                  'topOfTotalDifferencePoints': round(native['Total'][1] - reference['Total'][1], 3)}, ensure_ascii=False))
