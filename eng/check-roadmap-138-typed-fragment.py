#!/usr/bin/env python3
"""Check independently rendered typed fragments and their isolated local stores."""

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
        assert len(controls) == 2
        identities, bindings, values = [], [], []
        for control in controls:
            properties = control.find(w + 'sdtPr')
            identities.append(properties.find(w + 'id').get(w + 'val'))
            bindings.append(properties.find(w + 'dataBinding').get(w + 'storeItemID'))
            values.append(''.join(control.find(w + 'sdtContent').itertext()))
        assert len(set(identities)) == len(set(bindings)) == 2
        assert values == ['Bea', 'Ada']
        stores = {}
        for index in (1, 2):
            props = ET.fromstring(document.read(f'customXml/wordnet-fragment-props-{index}.xml'))
            data = ET.fromstring(document.read(f'customXml/wordnet-fragment-{index}.xml'))
            stores[props.get(ds + 'itemID')] = data.find(p + 'customer').get('name')
        assert [stores[key] for key in bindings] == values
    text = args.pdf_text.read_text(encoding='utf-8')
    first, second = text.index('Name: Bea'), text.index('Name: Ada')
    assert first < second
    print(json.dumps({'controlIds': identities, 'distinctStores': len(set(bindings)),
                      'storeValues': values, 'referenceText': ['Name: Bea', 'Name: Ada']}))


if __name__ == '__main__':
    main()
