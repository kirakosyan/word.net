#!/usr/bin/env python3
"""Check copied glossary resource bytes and a restored styled placeholder."""

import argparse
import hashlib
import json
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('input_docx', type=Path)
parser.add_argument('output_docx', type=Path)
parser.add_argument('pdf_text', type=Path)
args = parser.parse_args()
w = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
r = '{http://schemas.openxmlformats.org/package/2006/relationships}'
kinds = ('styles', 'settings', 'fontTable', 'webSettings')
hashes = {}
with zipfile.ZipFile(args.input_docx) as source, zipfile.ZipFile(args.output_docx) as output:
    relationships = ET.fromstring(output.read('word/glossary/_rels/wordnet-fragment-1.xml.rels'))
    links = relationships.findall(r + 'Relationship')
    assert len(links) == len(kinds)
    for kind in kinds:
        path = f'word/glossary/{kind}.xml'
        before = source.read(path)
        after = output.read(path)
        assert before == after
        matching = [link for link in links if link.get('Type', '').endswith('/' + kind)]
        assert len(matching) == 1 and matching[0].get('Target') == '/' + path
        assert matching[0].get('TargetMode') is None
        hashes[kind] = hashlib.sha256(after).hexdigest()
    glossary = ET.fromstring(output.read('word/glossary/wordnet-fragment-1.xml'))
    entries = glossary.findall('.//' + w + 'docPart')
    assert len(entries) == 2
    for entry in entries:
        assert entry.find('.//' + w + 'docPartBody//' + w + 'color').get(w + 'val') == '808080'
    main = ET.fromstring(output.read('word/document.xml'))
    controls = main.findall('.//' + w + 'sdt')
    assert len(controls) == 2
    assert [''.join(control.find(w + 'sdtContent').itertext()) for control in controls] == ['Edited', 'Choose']
    assert controls[1].find('.//' + w + 'showingPlcHdr') is not None
    assert controls[1].find('.//' + w + 'sdtContent//' + w + 'color').get(w + 'val') == '808080'
text = args.pdf_text.read_text(encoding='utf-8')
assert text.index('Name: Edited') < text.index('Name: Choose')
print(json.dumps({'resourceSha256': hashes, 'placeholderColor': '808080', 'referenceText': ['Name: Edited', 'Name: Choose']}))
