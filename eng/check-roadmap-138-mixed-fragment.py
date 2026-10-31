#!/usr/bin/env python3
"""Check the mixed fragment's local binding and independently rendered text order."""

import argparse
import json
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('docx', type=Path)
    parser.add_argument('pdf_text', type=Path)
    args = parser.parse_args()
    w = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
    ds = '{http://schemas.openxmlformats.org/officeDocument/2006/customXml}'
    p = '{urn:wordnet:controls}'
    with zipfile.ZipFile(args.docx) as document:
        root = ET.fromstring(document.read('word/document.xml'))
        controls = root.findall('.//' + w + 'sdt')
        assert len(controls) == 1
        control = controls[0]
        properties = control.find(w + 'sdtPr')
        store = properties.find(w + 'dataBinding').get(w + 'storeItemID')
        assert ''.join(control.find(w + 'sdtContent').itertext()) == 'Bea'
        props = ET.fromstring(document.read('customXml/wordnet-fragment-props-1.xml'))
        data = ET.fromstring(document.read('customXml/wordnet-fragment-1.xml'))
        assert props.get(ds + 'itemID') == store
        assert data.find(p + 'customer').get('name') == 'Bea'
    text = args.pdf_text.read_text(encoding='utf-8')
    expected = ('Before', 'Name: Bea', 'Quarterly', 'Sales', 'Q1', 'Q2', '\nx\n4\n', 'After')
    positions = [text.index(item) for item in expected]
    assert positions == sorted(positions)
    print(json.dumps({'controlValue': 'Bea', 'bindingValue': 'Bea', 'referenceText': list(expected)}))


if __name__ == '__main__':
    main()
