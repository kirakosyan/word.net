#!/usr/bin/env python3
"""Check glossary identities, placeholder references and independent display."""

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
    rel = '{http://schemas.openxmlformats.org/package/2006/relationships}'
    types = '{http://schemas.openxmlformats.org/package/2006/content-types}'
    with zipfile.ZipFile(args.docx) as document:
        main = ET.fromstring(document.read('word/document.xml'))
        entries = ET.fromstring(document.read('word/glossary/wordnet-fragment-1.xml'))
        relationships = ET.fromstring(document.read('word/_rels/document.xml.rels'))
        glossary_links = [item for item in relationships.findall(rel + 'Relationship') if item.get('Type', '').endswith('/glossaryDocument')]
        assert len(glossary_links) == 1 and glossary_links[0].get('Target') == '/word/glossary/wordnet-fragment-1.xml'
        assert glossary_links[0].get('TargetMode') is None
        content_types = ET.fromstring(document.read('[Content_Types].xml'))
        overrides = [item for item in content_types.findall(types + 'Override') if item.get('PartName') == '/word/glossary/wordnet-fragment-1.xml']
        assert len(overrides) == 1 and overrides[0].get('ContentType') == 'application/vnd.openxmlformats-officedocument.wordprocessingml.document.glossary+xml'
        controls = main.findall('.//' + w + 'sdt')
        refs = [control.find(w + 'sdtPr/' + w + 'placeholder/' + w + 'docPart').get(w + 'val') for control in controls]
        values = [''.join(control.find(w + 'sdtContent').itertext()) for control in controls]
        properties = entries.findall('.//' + w + 'docPartPr')
        names = [item.find(w + 'name').get(w + 'val') for item in properties]
        guids = [item.find(w + 'guid').get(w + 'val') for item in properties]
        assert len(controls) == len(set(refs)) == len(set(names)) == len(set(guids)) == 2
        assert refs == names and values == ['Edited', 'Choose']
        assert [''.join(entry.find(w + 'docPartBody').itertext()) for entry in entries.findall('.//' + w + 'docPart')] == ['Choose', 'Choose']
    text = args.pdf_text.read_text(encoding='utf-8')
    assert text.index('Name: Edited') < text.index('Name: Choose')
    print(json.dumps({'entries': names, 'distinctGuids': len(set(guids)), 'values': values,
                      'referenceText': ['Name: Edited', 'Name: Choose']}))


if __name__ == '__main__':
    main()
