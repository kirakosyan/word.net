"""Assemble the nested-header and empty-glossary control from the licensed fixture parts."""
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZIP_STORED, ZipFile, ZipInfo

here = Path(__file__).resolve().parent
w = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
r = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
ET.register_namespace('w', w)

with ZipFile(here / 'stories.docx') as archive:
    parts = {name: archive.read(name) for name in archive.namelist()}
with ZipFile(here.parent / 'NestedOwners' / 'nested.docx') as archive:
    parent = ET.fromstring(archive.read('word/document.xml')).find(f'{{{w}}}body/{{{w}}}sdt')
header = ET.Element(f'{{{w}}}hdr')
header.append(parent)
parts['word/header1.xml'] = ET.tostring(header, encoding='utf-8')
parts['word/glossary/document.xml'] = ET.tostring(ET.Element(f'{{{w}}}glossaryDocument'), encoding='utf-8')

for path, tag, attributes in [
        ('word/_rels/document.xml.rels', 'Relationship',
         dict(Id='glossary', Type=r + '/glossaryDocument', Target='glossary/document.xml')),
        ('[Content_Types].xml', 'Override',
         dict(PartName='/word/glossary/document.xml',
              ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.glossary+xml'))]:
    root = ET.fromstring(parts[path])
    namespace = root.tag[1:].split('}')[0]
    ET.register_namespace('', namespace)
    ET.SubElement(root, f'{{{namespace}}}{tag}', attributes)
    parts[path] = ET.tostring(root, encoding='utf-8')

with ZipFile(here / 'nested-empty-glossary.docx', 'w') as archive:
    for name, content in sorted(parts.items()):
        entry = ZipInfo(name, (2000, 1, 1, 0, 0, 0))
        entry.create_system = 0
        archive.writestr(entry, content, compress_type=ZIP_STORED)
