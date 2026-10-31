#!/usr/bin/env python3
"""Check a generated block form's OOXML ownership, identities and reference text."""

import argparse
import json
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('docx', type=Path)
parser.add_argument('pdf_text', type=Path)
args = parser.parse_args()
w = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
w14 = '{http://schemas.microsoft.com/office/word/2010/wordml}'
with zipfile.ZipFile(args.docx) as package:
    document = ET.fromstring(package.read('word/document.xml'))
    body = document.find(w + 'body')
    controls = body.findall(w + 'sdt')
    assert len(controls) == 6 and not body.findall(w + 'p')
    properties = [item.find(w + 'sdtPr') for item in controls]
    ids = [int(item.find(w + 'id').get(w + 'val')) for item in properties]
    assert len(set(ids)) == len(ids)
    types = [(w + 'text'), (w14 + 'checkbox'), (w + 'dropDownList'), (w + 'comboBox'), (w + 'date'), (w + 'richText')]
    assert all(prop.find(kind) is not None for prop, kind in zip(properties, types))
    values = []
    for item in controls:
        content = item.find(w + 'sdtContent')
        assert len(content.findall(w + 'p')) == 1
        values.append(''.join(content.itertext()))
    assert values == ['Ada', 'X', 'Beta', 'Manual', '2026-10-31', 'Approved']
text = args.pdf_text.read_text(encoding='utf-8')
positions = [text.index(value) for value in values]
assert positions == sorted(positions)
print(json.dumps({'ids': ids, 'types': [kind.split('}')[-1] for kind in types], 'values': values}))
